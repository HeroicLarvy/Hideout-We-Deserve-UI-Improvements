using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HideoutWeDeserveUIImprovements;

[BepInPlugin(PluginId, PluginName, PluginVersion)]
public sealed class HideoutWeDeserveUIImprovementsPlugin : BaseUnityPlugin
{
    private const string PluginId = "com.heroiclarvy.hideoutwedeserveuiimprovements";
    private const string PluginName = "Hideout We Deserve - UI Improvements";
    private const string PluginVersion = "1.0.0";
    private const float HoverItemGap = 4.5f;
    private const float HoverProgressWidth = 104f;

    private static HideoutWeDeserveUIImprovementsPlugin _instance;
    private static ConfigEntry<bool> _hideBottomScrollbar;
    private static ConfigEntry<bool> _showUpgradeRequirementsOnHover;
    private static ConfigEntry<bool> _showActiveCraftOnHover;
    private static ConfigEntry<bool> _showItemNamesOnHover;
    private static ConfigEntry<bool> _openStationMenuWithoutZoom;
    private static AreaWorldPanel _hoveredWorldPanel;
    private static AreasPanel _configuredAreasPanel;
    private static CanvasGroup _areasPanelCanvasGroup;
    private static float _areasPanelOriginalAlpha;
    private static bool _areasPanelOriginalBlocksRaycasts;
    private static bool _areasPanelOriginalInteractable;
    private static float _nextRequirementRefreshAt;
    private static readonly Dictionary<string, ItemIcon> ItemIcons = new Dictionary<string, ItemIcon>();
    private static readonly Dictionary<string, Item> RecipeOutputItems = new Dictionary<string, Item>();
    private static readonly Dictionary<string, Task<Sprite>> TraderAvatarTasks = new Dictionary<string, Task<Sprite>>();
    private static readonly HashSet<string> TraderAvatarWarnings = new HashSet<string>();
    private static readonly HashSet<string> MissingRecipeWarnings = new HashSet<string>();
    private static GUISkin _tooltipStyleSkin;
    private static Font _tooltipFont;
    private static GUIStyle _tooltipSectionStyle;
    private static GUIStyle _tooltipBodyStyle;
    private static GUIStyle _tooltipCountStyle;
    private static GUIStyle _tooltipTimerStyle;
    private static GUIStyle _tooltipProgressStyle;
    private Harmony _harmony;

    private sealed class UpgradeStatus
    {
        public readonly Sprite Icon;
        public readonly Profile.TraderInfo Trader;
        public readonly int? Level;
        public readonly bool Fulfilled;

        public UpgradeStatus(Sprite icon, Profile.TraderInfo trader, int? level, bool fulfilled)
        {
            Icon = icon;
            Trader = trader;
            Level = level;
            Fulfilled = fulfilled;
        }
    }

    private sealed class DefectiveWallRequirements
    {
        public readonly bool FloorMopped;
        public readonly ItemRequirement MopItem;
        public readonly List<ItemRequirement> DismantleItems;

        public DefectiveWallRequirements(bool floorMopped, ItemRequirement mopItem, List<ItemRequirement> dismantleItems)
        {
            FloorMopped = floorMopped;
            MopItem = mopItem;
            DismantleItems = dismantleItems;
        }
    }

    private sealed class ActiveCraft
    {
        public readonly string OutputName;
        public readonly Item OutputItem;
        public readonly List<ItemRequirement> Ingredients;
        public readonly float Progress;
        public readonly float EstimatedTimeLeft;
        public readonly bool IsWorking;
        public readonly bool IsCompleted;

        public ActiveCraft(
            string outputName,
            Item outputItem,
            List<ItemRequirement> ingredients,
            float progress,
            float estimatedTimeLeft,
            bool isWorking,
            bool isCompleted)
        {
            OutputName = outputName;
            OutputItem = outputItem;
            Ingredients = ingredients;
            Progress = progress;
            EstimatedTimeLeft = estimatedTimeLeft;
            IsWorking = isWorking;
            IsCompleted = isCompleted;
        }
    }

    private void Awake()
    {
        _instance = this;
        _hideBottomScrollbar = Config.Bind(
            "Hideout",
            "HideBottomScrollbar",
            false,
            "Hide the entire bottom hideout station bar.");
        _showUpgradeRequirementsOnHover = Config.Bind(
            "Hideout",
            "ShowUpgradeRequirementsOnHover",
            true,
            "Show the next-level item requirements when hovering a station in the hideout.");
        _showActiveCraftOnHover = Config.Bind(
            "Hideout",
            "ShowActiveCraftOnHover",
            true,
            "Show the active craft and time remaining when hovering its station in the hideout.");
        _showItemNamesOnHover = Config.Bind(
            "Hideout",
            "ShowItemNamesOnHover",
            false,
            "Show item names alongside item artwork in hideout hover details.");
        _openStationMenuWithoutZoom = Config.Bind(
            "Hideout",
            "OpenStationMenuWithoutZoom",
            false,
            "Open the station menu when clicking an in-hideout station without zooming the camera.");
        _hideBottomScrollbar.SettingChanged += OnBottomBarSettingChanged;
        _harmony = new Harmony(PluginId);
        _harmony.Patch(
            AccessTools.Method(typeof(HideoutScreenOverlay), nameof(HideoutScreenOverlay.Show)),
            postfix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnHideoutScreenShown)));
        _harmony.Patch(
            AccessTools.Method(typeof(AreaWorldPanel), nameof(AreaWorldPanel.PointerEnterHandler)),
            postfix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnWorldPanelPointerEntered)));
        _harmony.Patch(
            AccessTools.Method(typeof(AreaWorldPanel), nameof(AreaWorldPanel.PointerExitHandler)),
            postfix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnWorldPanelPointerExited)));
        _harmony.Patch(
            AccessTools.Method(typeof(AreaWorldPanel), nameof(AreaWorldPanel.OnPointerClick)),
            prefix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnWorldPanelPointerClicked)));

        Logger.LogInfo($"{PluginName} loaded.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _hideBottomScrollbar.SettingChanged -= OnBottomBarSettingChanged;
        if (_areasPanelCanvasGroup != null)
        {
            RestoreBottomBarVisibility();
        }

        _instance = null;
        _hoveredWorldPanel = null;
        ItemIcons.Clear();
        RecipeOutputItems.Clear();
        MissingRecipeWarnings.Clear();
        TraderAvatarTasks.Clear();
        TraderAvatarWarnings.Clear();
    }

    private void OnGUI()
    {
        DrawWorldPanelTooltip();
    }

    private static void OnHideoutScreenShown(HideoutScreenOverlay __instance)
    {
        ApplyBottomBarVisibility(__instance);
    }

    private static void OnWorldPanelPointerEntered(AreaWorldPanel __instance)
    {
        if (_hoveredWorldPanel != __instance)
        {
            _nextRequirementRefreshAt = 0f;
        }

        _hoveredWorldPanel = __instance;
    }

    private static void OnWorldPanelPointerExited(AreaWorldPanel __instance)
    {
        if (_hoveredWorldPanel == __instance)
        {
            _hoveredWorldPanel = null;
        }
    }

    private static bool OnWorldPanelPointerClicked(AreaWorldPanel __instance, UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (!_openStationMenuWithoutZoom.Value ||
            eventData.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left ||
            __instance.Data == null)
        {
            return true;
        }

        FieldInfo firstPersonModeField = AccessTools.Field(typeof(AreaWorldPanel), "_firstPersonMode");
        if (firstPersonModeField?.GetValue(__instance) is not bool firstPersonMode || firstPersonMode)
        {
            return true;
        }

        HideoutScreenOverlay overlay = MonoBehaviourSingleton<CommonUI>.Instantiated
            ? MonoBehaviourSingleton<CommonUI>.Instance.HideoutScreenOverlay
            : null;
        if (overlay == null)
        {
            _instance.Logger.LogWarning("Could not find the hideout overlay; cannot open the station menu without zooming.");
            return false;
        }

        FieldInfo canSelectAreaField = AccessTools.Field(typeof(HideoutScreenOverlay), "_canSelectArea");
        if (canSelectAreaField?.GetValue(overlay) is not bool canSelectArea || !canSelectArea)
        {
            return false;
        }

        FieldInfo substrateField = AccessTools.Field(typeof(HideoutScreenOverlay), "_areaSubstrate");
        if (substrateField?.GetValue(overlay) is not AreaScreenSubstrate substrate)
        {
            _instance.Logger.LogWarning("Could not find the hideout station menu; cannot open it without zooming.");
            return false;
        }

        substrate.SelectArea(__instance.Data);
        return false;
    }

    private static void DrawWorldPanelTooltip()
    {
        if (_hoveredWorldPanel == null || _hoveredWorldPanel.Data == null)
        {
            return;
        }

        AreaData areaData = _hoveredWorldPanel.Data;
        ActiveCraft craft = _showActiveCraftOnHover.Value ? GetActiveCraft(areaData) : null;
        var upgradeRequirements = new List<Requirement>();
        DefectiveWallRequirements wallRequirements = null;
        if (_showUpgradeRequirementsOnHover.Value)
        {
            if (areaData.Template.Type == EAreaType.EmergencyWall)
            {
                wallRequirements = GetDefectiveWallRequirements(areaData);
            }
            else
            {
                upgradeRequirements.AddRange(areaData.NextStage.Requirements?
                    .Where(requirement =>
                        requirement is ItemRequirement ||
                        requirement is AreaRequirement ||
                        requirement is TraderLoyaltyRequirement ||
                        requirement is TraderUnlockRequirement)
                    .Where(requirement => requirement is not AreaRequirement areaRequirement ||
                        areaRequirement.AreaType != areaData.Template.Type)
                    ?? Enumerable.Empty<Requirement>());
            }
        }

        if (Time.realtimeSinceStartup >= _nextRequirementRefreshAt)
        {
            UpdateUpgradeRequirements(upgradeRequirements);
            if (wallRequirements != null)
            {
                UpdateUpgradeRequirements(
                    (wallRequirements.MopItem == null
                        ? Enumerable.Empty<Requirement>()
                        : new Requirement[] { wallRequirements.MopItem })
                    .Concat(wallRequirements.DismantleItems));
            }

            _nextRequirementRefreshAt = Time.realtimeSinceStartup + 1f;
        }

        if (upgradeRequirements.Count == 0 && wallRequirements == null && craft == null)
        {
            return;
        }

        DrawHoverTooltip(areaData, upgradeRequirements, craft, wallRequirements);
    }

    private static void DrawHoverTooltip(
        AreaData areaData,
        List<Requirement> upgradeRequirements,
        ActiveCraft craft,
        DefectiveWallRequirements wallRequirements)
    {
        EnsureTooltipStyles();
        const float maxWidth = 1120f;
        const float padding = 12f;
        const float sectionPadding = 8f;
        const float sectionHeight = 28f;
        const float sectionGap = 10f;
        bool showNames = _showItemNamesOnHover.Value;
        float itemWidth = showNames ? 108f : 78f;
        float itemHeight = showNames ? 136f : 110f;
        List<ItemRequirement> itemRequirements = upgradeRequirements.OfType<ItemRequirement>().ToList();
        List<UpgradeStatus> upgradeStatuses = GetUpgradeStatuses(upgradeRequirements);
        List<float> upgradeStatusWidths = Enumerable.Repeat(58f, upgradeStatuses.Count).ToList();
        bool showWallMopItem = wallRequirements != null && !wallRequirements.FloorMopped && wallRequirements.MopItem != null;
        string wallMopHeading = wallRequirements == null
            ? string.Empty
            : wallRequirements.FloorMopped
                ? "Mop the floor: The floor has been mopped"
                : wallRequirements.MopItem != null &&
                  wallRequirements.MopItem.UserItemsCount >= wallRequirements.MopItem.IntCount
                    ? "Mop the floor: Available"
                    : "Mop the floor:";
        bool showWallDismantle = wallRequirements != null && wallRequirements.DismantleItems.Count > 0;
        string timeDisplay = craft == null ? string.Empty : GetTimeDisplay(craft);
        float timeWidth = craft == null ? 0f : MeasureTimeDisplayWidth(timeDisplay);
        string upgradeHeading = upgradeRequirements.Count == 0
            ? string.Empty
            : GetUpgradeHeading(areaData);
        float screenAvailableWidth = Mathf.Min(maxWidth, Screen.width - 40f) - padding * 2f - sectionPadding * 2f;
        float contentWidth = 0f;
        if (craft != null)
        {
            contentWidth = Mathf.Max(contentWidth, MeasureActiveCraftWidth(craft, itemWidth, timeWidth));
            contentWidth = Mathf.Max(contentWidth, MeasureTextWidth("Production:", _tooltipSectionStyle));
        }

        if (itemRequirements.Count > 0)
        {
            contentWidth = Mathf.Max(contentWidth, itemRequirements.Count * (itemWidth + HoverItemGap) - HoverItemGap);
        }

        if (upgradeStatuses.Count > 0)
        {
            contentWidth = Mathf.Max(contentWidth, MeasureFlowWidth(upgradeStatusWidths, HoverItemGap));
        }

        if (upgradeRequirements.Count > 0)
        {
            contentWidth = Mathf.Max(contentWidth, MeasureTextWidth(upgradeHeading, _tooltipSectionStyle));
        }

        if (wallRequirements != null)
        {
            contentWidth = Mathf.Max(contentWidth, MeasureTextWidth(wallMopHeading, _tooltipSectionStyle));
            contentWidth = Mathf.Max(contentWidth, MeasureTextWidth("Dismantle:", _tooltipSectionStyle));
            if (showWallMopItem || showWallDismantle)
            {
                contentWidth = Mathf.Max(contentWidth, itemWidth);
            }
        }

        float availableWidth = Mathf.Min(screenAvailableWidth, contentWidth);
        float panelWidth = availableWidth + sectionPadding * 2f;
        float width = panelWidth + padding * 2f;
        float height = padding * 2f;

        if (craft != null)
        {
            height += sectionPadding * 2f + sectionHeight + sectionGap + MeasureActiveCraftHeight(
                craft,
                availableWidth,
                itemWidth,
                itemHeight,
                timeWidth);
        }

        if (upgradeRequirements.Count > 0)
        {
            height += sectionPadding * 2f + sectionHeight + sectionGap;
            if (itemRequirements.Count > 0)
            {
                height += MeasureItemFlowHeight(
                    itemRequirements.Count,
                    itemWidth,
                    availableWidth,
                    itemHeight);
            }

            if (upgradeStatuses.Count > 0)
            {
                if (itemRequirements.Count > 0)
                {
                    height += sectionGap;
                }

                height += MeasureFlowHeight(upgradeStatusWidths, availableWidth, 58f, HoverItemGap);
            }
        }

        if (wallRequirements != null)
        {
            height += sectionPadding * 2f + sectionHeight + sectionGap;
            if (showWallMopItem)
            {
                height += itemHeight;
            }

            if (showWallDismantle)
            {
                height += sectionGap + sectionHeight + sectionGap + MeasureItemFlowHeight(
                    wallRequirements.DismantleItems.Count,
                    itemWidth,
                    availableWidth,
                    itemHeight);
            }
        }

        int sectionCount = (craft == null ? 0 : 1) +
            (upgradeRequirements.Count == 0 ? 0 : 1) +
            (wallRequirements == null ? 0 : 1);
        height += Mathf.Max(0, sectionCount - 1) * sectionGap;

        float scale = Mathf.Min(1f, (Screen.width - 24f) / width, (Screen.height - 24f) / height);
        float renderedWidth = width * scale;
        float renderedHeight = height * scale;
        float left = Mathf.Clamp(Input.mousePosition.x + 18f, 12f, Screen.width - renderedWidth - 12f);
        float top = Screen.height - Input.mousePosition.y + 18f;
        if (top + renderedHeight > Screen.height - 12f)
        {
            top = Screen.height - Input.mousePosition.y - renderedHeight - 18f;
        }

        top = Mathf.Clamp(top, 12f, Screen.height - renderedHeight - 12f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(left, top, 0f), Quaternion.identity, new Vector3(scale, scale, 1f)) * previousMatrix;
        GUI.Box(new Rect(0f, 0f, width, height), GUIContent.none);

        GUI.BeginGroup(new Rect(0f, 0f, width, height));
        float currentY = padding;
        float sectionLeft = padding;
        float contentLeft = sectionLeft + sectionPadding;

        if (craft != null)
        {
            float craftHeight = sectionPadding * 2f + sectionHeight + sectionGap + MeasureActiveCraftHeight(
                craft,
                availableWidth,
                itemWidth,
                itemHeight,
                timeWidth);
            DrawSectionPanel(new Rect(sectionLeft, currentY, panelWidth, craftHeight));
            currentY += sectionPadding;
            DrawOutlinedLabel(new Rect(contentLeft, currentY, availableWidth, sectionHeight), "Production:", _tooltipSectionStyle);
            currentY += sectionHeight + sectionGap;
            currentY += DrawActiveCraftStrip(contentLeft, currentY, availableWidth, craft, timeDisplay, itemWidth, itemHeight, timeWidth);
            currentY += sectionPadding;
            if (upgradeRequirements.Count > 0 || wallRequirements != null)
            {
                currentY += sectionGap;
            }
        }

        if (upgradeRequirements.Count > 0)
        {
            float upgradeHeight = sectionPadding * 2f;
            upgradeHeight += sectionHeight + sectionGap;
            if (itemRequirements.Count > 0)
            {
                upgradeHeight += MeasureItemFlowHeight(itemRequirements.Count, itemWidth, availableWidth, itemHeight);
            }

            if (upgradeStatuses.Count > 0)
            {
                if (itemRequirements.Count > 0)
                {
                    upgradeHeight += sectionGap;
                }

                upgradeHeight += MeasureFlowHeight(upgradeStatusWidths, availableWidth, 58f, HoverItemGap);
            }

            DrawSectionPanel(new Rect(sectionLeft, currentY, panelWidth, upgradeHeight));
            currentY += sectionPadding;
            DrawOutlinedLabel(
                new Rect(contentLeft, currentY, availableWidth, sectionHeight),
                upgradeHeading,
                _tooltipSectionStyle);
            currentY += sectionHeight + sectionGap;
            if (itemRequirements.Count > 0)
            {
                currentY += DrawItemStrip(contentLeft, currentY, availableWidth, itemRequirements, itemWidth, itemHeight);
            }

            if (upgradeStatuses.Count > 0)
            {
                if (itemRequirements.Count > 0)
                {
                    currentY += sectionGap;
                }

                currentY += DrawUpgradeStatuses(contentLeft, currentY, availableWidth, upgradeStatuses, upgradeStatusWidths);
            }

            currentY += sectionPadding;
            if (wallRequirements != null)
            {
                currentY += sectionGap;
            }
        }

        if (wallRequirements != null)
        {
            float wallHeight = sectionPadding * 2f + sectionHeight + sectionGap;
            if (showWallMopItem)
            {
                wallHeight += itemHeight;
            }

            if (showWallDismantle)
            {
                wallHeight += sectionGap + sectionHeight + sectionGap +
                    MeasureItemFlowHeight(wallRequirements.DismantleItems.Count, itemWidth, availableWidth, itemHeight);
            }

            DrawSectionPanel(new Rect(sectionLeft, currentY, panelWidth, wallHeight));
            currentY += sectionPadding;
            DrawOutlinedLabel(
                new Rect(contentLeft, currentY, availableWidth, sectionHeight),
                wallMopHeading,
                _tooltipSectionStyle);
            currentY += sectionHeight + sectionGap;
            if (showWallMopItem)
            {
                currentY += DrawItemStrip(
                    contentLeft,
                    currentY,
                    availableWidth,
                    new List<ItemRequirement> { wallRequirements.MopItem },
                    itemWidth,
                    itemHeight);
            }

            if (showWallDismantle)
            {
                currentY += sectionGap;
                DrawOutlinedLabel(
                    new Rect(contentLeft, currentY, availableWidth, sectionHeight),
                    "Dismantle:",
                    _tooltipSectionStyle);
                currentY += sectionHeight + sectionGap;
                currentY += DrawItemStrip(
                    contentLeft,
                    currentY,
                    availableWidth,
                    wallRequirements.DismantleItems,
                    itemWidth,
                    itemHeight);
            }

            currentY += sectionPadding;
        }

        GUI.EndGroup();
        GUI.matrix = previousMatrix;
    }

    private static float DrawActiveCraftStrip(
        float left,
        float top,
        float availableWidth,
        ActiveCraft craft,
        string timeDisplay,
        float itemWidth,
        float itemHeight,
        float timeWidth)
    {
        const float gap = HoverItemGap;
        const float progressWidth = HoverProgressWidth;
        bool showNames = _showItemNamesOnHover.Value;
        List<float> widths = GetActiveCraftEntryWidths(craft, itemWidth, progressWidth, timeWidth);
        for (int i = 0; i < craft.Ingredients.Count; i++)
        {
            ItemRequirement ingredient = craft.Ingredients[i];
            Rect itemRect = GetCenteredFlowEntry(widths, i, left, top, itemHeight, availableWidth, gap);
            DrawItemCard(itemRect, ingredient.Item, ingredient.ItemName, string.Empty, showNames);
        }

        int progressIndex = craft.Ingredients.Count;
        Rect progressRect = GetCenteredFlowEntry(widths, progressIndex, left, top, itemHeight, availableWidth, gap);
        DrawOutlinedLabel(
            progressRect,
            $"{Mathf.Clamp01(craft.Progress):P0}",
            _tooltipProgressStyle);

        int outputIndex = progressIndex + 1;
        Rect outputRect = GetCenteredFlowEntry(widths, outputIndex, left, top, itemHeight, availableWidth, gap);
        DrawItemCard(
            outputRect,
            craft.OutputItem,
            craft.OutputName,
            string.Empty,
            showNames);

        int timeIndex = outputIndex + 1;
        Rect timeRect = GetCenteredFlowEntry(widths, timeIndex, left, top, itemHeight, availableWidth, gap);
        DrawOutlinedLabel(timeRect, timeDisplay, _tooltipTimerStyle);
        return MeasureFlowHeight(
            widths,
            availableWidth,
            itemHeight,
            gap);
    }

    private static float DrawItemStrip(
        float left,
        float top,
        float availableWidth,
        List<ItemRequirement> requirements,
        float itemWidth,
        float itemHeight)
    {
        const float gap = HoverItemGap;
        bool showNames = _showItemNamesOnHover.Value;
        List<ItemRequirement> itemRequirements = requirements.OfType<ItemRequirement>().ToList();
        IList<float> widths = Enumerable.Repeat(itemWidth, itemRequirements.Count).ToList();
        for (int i = 0; i < itemRequirements.Count; i++)
        {
            ItemRequirement requirement = itemRequirements[i];
            int availableCount = Mathf.Clamp(requirement.UserItemsCount, 0, requirement.IntCount);
            Rect itemRect = GetCenteredFlowEntry(widths, i, left, top, itemHeight, availableWidth, gap);
            DrawItemCard(
                itemRect,
                requirement.Item,
                requirement.ItemName,
                $"{availableCount}/{requirement.IntCount}",
                showNames,
                availableCount >= requirement.IntCount);
        }

        return MeasureItemFlowHeight(requirements.Count, itemWidth, availableWidth, itemHeight);
    }

    private static float MeasureActiveCraftHeight(
        ActiveCraft craft,
        float availableWidth,
        float itemWidth,
        float itemHeight,
        float timeWidth)
    {
        return MeasureFlowHeight(
            GetActiveCraftEntryWidths(craft, itemWidth, HoverProgressWidth, timeWidth),
            availableWidth,
            itemHeight,
            HoverItemGap);
    }

    private static List<float> GetActiveCraftEntryWidths(ActiveCraft craft, float itemWidth, float progressWidth, float timeWidth)
    {
        var widths = Enumerable.Repeat(itemWidth, craft.Ingredients.Count).ToList();
        widths.Add(progressWidth);
        widths.Add(itemWidth);
        widths.Add(timeWidth);
        return widths;
    }

    private static float MeasureItemFlowHeight(int count, float itemWidth, float availableWidth, float itemHeight)
    {
        return MeasureFlowHeight(
            Enumerable.Repeat(itemWidth, count).ToList(),
            availableWidth,
            itemHeight,
            HoverItemGap);
    }

    private static float MeasureFlowWidth(IList<float> widths, float gap)
    {
        return widths.Count == 0 ? 0f : widths.Sum() + gap * (widths.Count - 1);
    }

    private static float DrawUpgradeStatuses(
        float left,
        float top,
        float availableWidth,
        IList<UpgradeStatus> statuses,
        IList<float> widths)
    {
        const float gap = HoverItemGap;
        for (int i = 0; i < statuses.Count; i++)
        {
            Rect statusRect = GetCenteredFlowEntry(widths, i, left, top, 58f, availableWidth, gap);
            Color borderColor = statuses[i].Fulfilled
                ? new Color(0.10f, 0.62f, 1f, 1f)
                : new Color(1f, 0.12f, 0.08f, 1f);
            DrawBorder(statusRect, borderColor, 3f);

            Sprite icon = statuses[i].Trader != null
                ? GetTraderAvatar(statuses[i].Trader)
                : statuses[i].Icon;
            if (icon != null)
            {
                DrawSprite(new Rect(statusRect.x + 3f, statusRect.y + 3f, statusRect.width - 6f, statusRect.height - 6f), icon, 1f);
            }

            if (statuses[i].Level.HasValue)
            {
                var levelRect = new Rect(statusRect.x + 2f, statusRect.y + 35f, statusRect.width - 4f, 21f);
                Color previousColor = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(levelRect, Texture2D.whiteTexture);
                GUI.color = previousColor;
                DrawOutlinedLabel(levelRect, statuses[i].Level.Value.ToString(), _tooltipBodyStyle);
            }
        }

        return MeasureFlowHeight(widths, availableWidth, 58f, gap);
    }

    private static List<UpgradeStatus> GetUpgradeStatuses(IEnumerable<Requirement> requirements)
    {
        var statuses = new List<UpgradeStatus>();
        foreach (Requirement requirement in requirements)
        {
            if (requirement is AreaRequirement areaRequirement)
            {
                AreaData requiredArea = areaRequirement.AreaData;
                statuses.Add(new UpgradeStatus(
                    requiredArea?.Template.AreaIcon,
                    null,
                    areaRequirement.RequiredLevel,
                    areaRequirement.Fulfilled));
            }
            else if (requirement is TraderLoyaltyRequirement loyaltyRequirement)
            {
                statuses.Add(new UpgradeStatus(
                    null,
                    loyaltyRequirement.Trader,
                    loyaltyRequirement.LoyaltyLevel,
                    loyaltyRequirement.Fulfilled));
            }
            else if (requirement is TraderUnlockRequirement unlockRequirement)
            {
                statuses.Add(new UpgradeStatus(
                    null,
                    unlockRequirement.Trader,
                    null,
                    unlockRequirement.Fulfilled));
            }
        }

        return statuses;
    }

    private static string GetUpgradeHeading(AreaData areaData)
    {
        return areaData.CurrentLevel <= 0
            ? "Construction Requirements"
            : $"Level {areaData.NextStage.Level} Upgrade Requirements";
    }

    private static DefectiveWallRequirements GetDefectiveWallRequirements(AreaData areaData)
    {
        Improvement mopImprovement = areaData.CurrentStage?.Improvements?.Data?.FirstOrDefault();
        if (mopImprovement == null && areaData.Template.Stages != null)
        {
            mopImprovement = areaData.Template.Stages.Values
                .Where(stage => stage?.Improvements?.Data != null)
                .SelectMany(stage => stage.Improvements.Data)
                .FirstOrDefault();
        }

        ItemRequirement mopItem = mopImprovement?.Requirements?.OfType<ItemRequirement>().FirstOrDefault();
        bool floorMopped = mopImprovement?.Completed ?? false;
        var dismantleItems = new List<ItemRequirement>();
        if (areaData.Template.Stages != null &&
            areaData.Template.Stages.TryGetValue(4, out Stage dismantleStage))
        {
            dismantleItems.AddRange(dismantleStage.Requirements?.OfType<ItemRequirement>()
                ?? Enumerable.Empty<ItemRequirement>());
        }

        return new DefectiveWallRequirements(floorMopped, mopItem, dismantleItems);
    }

    private static float MeasureActiveCraftWidth(ActiveCraft craft, float itemWidth, float timeWidth)
    {
        return craft.Ingredients.Count * (itemWidth + HoverItemGap) +
            HoverProgressWidth + HoverItemGap +
            itemWidth + HoverItemGap +
            timeWidth;
    }

    private static string GetTimeDisplay(ActiveCraft craft)
    {
        if (craft.IsCompleted || craft.EstimatedTimeLeft <= 0f)
        {
            return "Completed";
        }

        TimeSpan remaining = TimeSpan.FromSeconds(craft.EstimatedTimeLeft);
        string time = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        return craft.IsWorking ? $"Producing\n({time})" : $"Producing\n(Paused: {time})";
    }

    private static float MeasureTimeDisplayWidth(string displayText)
    {
        return displayText
            .Split('\n')
            .Max(line => MeasureTextWidth(line, _tooltipTimerStyle)) + 12f;
    }

    private static float MeasureTextWidth(string text, GUIStyle style)
    {
        return style.CalcSize(new GUIContent(text)).x + 4f;
    }

    private static float MeasureFlowHeight(IList<float> widths, float availableWidth, float itemHeight, float gap)
    {
        float x = 0f;
        float y = 0f;
        foreach (float entryWidth in widths)
        {
            if (x > 0f && x - gap + entryWidth > availableWidth)
            {
                x = 0f;
                y += itemHeight + gap;
            }

            x += entryWidth + gap;
        }

        return widths.Count == 0 ? 0f : y + itemHeight;
    }

    private static Rect GetCenteredFlowEntry(
        IList<float> widths,
        int entryIndex,
        float left,
        float top,
        float height,
        float availableWidth,
        float gap)
    {
        int rowStart = 0;
        float rowY = 0f;
        float rowWidth = 0f;
        for (int i = 0; i <= entryIndex; i++)
        {
            if (rowWidth > 0f && rowWidth + widths[i] > availableWidth)
            {
                rowStart = i;
                rowY += height + gap;
                rowWidth = 0f;
            }

            if (i < entryIndex)
            {
                rowWidth += widths[i] + (rowWidth == 0f ? 0f : gap);
            }
        }

        float totalRowWidth = 0f;
        float entryOffset = 0f;
        for (int i = rowStart; i < widths.Count; i++)
        {
            if (i > rowStart && totalRowWidth + gap + widths[i] > availableWidth)
            {
                break;
            }

            if (i < entryIndex)
            {
                entryOffset += widths[i] + gap;
            }

            totalRowWidth += widths[i] + (i == rowStart ? 0f : gap);
        }

        return new Rect(
            left + (availableWidth - totalRowWidth) / 2f + entryOffset,
            top + rowY,
            widths[entryIndex],
            height);
    }

    private static void DrawSectionPanel(Rect rect)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0.035f, 0.045f, 0.055f, 0.62f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
        DrawBorder(rect, new Color(0.68f, 0.78f, 0.86f, 0.82f), 1.5f);
    }

    private static void DrawItemCard(
        Rect cardRect,
        Item item,
        string itemName,
        string countText,
        bool showName,
        bool? completionStatus = null)
    {
        const float cardPadding = 4f;
        const float iconSize = 68f;
        float squareSize = Mathf.Min(iconSize + cardPadding * 2f, cardRect.width - 2f);
        float squareLeft = cardRect.x + (cardRect.width - squareSize) / 2f;
        float squareTop = cardRect.y + 4f;
        var squareRect = new Rect(squareLeft, squareTop, squareSize, squareSize);

        Color previousColor = GUI.color;
        GUI.color = GetItemBackgroundColor(item);
        GUI.DrawTexture(squareRect, Texture2D.whiteTexture);
        Color borderColor = completionStatus.HasValue
            ? completionStatus.Value
                ? new Color(0.10f, 0.62f, 1f, 1f)
                : new Color(1f, 0.12f, 0.08f, 1f)
            : new Color(0.06f, 0.06f, 0.06f, 1f);
        DrawBorder(squareRect, borderColor, completionStatus.HasValue ? 2f : 1f);
        GUI.color = previousColor;

        var iconRect = new Rect(
            squareRect.x + (squareSize - iconSize) / 2f,
            squareRect.y + (squareSize - iconSize) / 2f,
            iconSize,
            iconSize);
        DrawItemIcon(iconRect, item);

        float textTop = squareRect.yMax + 2f;
        float textHeight = Mathf.Max(14f, cardRect.yMax - textTop);
        if (showName && !string.IsNullOrEmpty(itemName))
        {
            DrawOutlinedLabel(
                new Rect(cardRect.x + 2f, textTop, cardRect.width - 4f, textHeight / (string.IsNullOrEmpty(countText) ? 1f : 2f)),
                itemName,
                _tooltipBodyStyle);
            if (!string.IsNullOrEmpty(countText))
            {
                textTop += textHeight / 2f;
                textHeight /= 2f;
            }
        }

        if (!string.IsNullOrEmpty(countText))
        {
            if (completionStatus.HasValue)
            {
                DrawOutlinedLabel(
                    new Rect(cardRect.x + 2f, textTop, cardRect.width - 4f, textHeight),
                    countText,
                    _tooltipCountStyle);
            }
            else
            {
                DrawOutlinedLabel(
                    new Rect(cardRect.x + 2f, textTop, cardRect.width - 4f, textHeight),
                    countText,
                    _tooltipBodyStyle);
            }
        }
    }

    private static Color GetItemBackgroundColor(Item item)
    {
        Color color = item == null
            ? new Color(0.25f, 0.25f, 0.25f, 1f)
            : JsonType.TaxonomyColorExtension.ToColor(item.BackgroundColor);
        color.a = 0.68f;
        return color;
    }

    private static void EnsureTooltipStyles()
    {
        if (_tooltipStyleSkin == GUI.skin && _tooltipSectionStyle != null)
        {
            return;
        }

        _tooltipStyleSkin = GUI.skin;
        if (_tooltipFont == null)
        {
            _tooltipFont = Resources.FindObjectsOfTypeAll<Font>()
                .FirstOrDefault(font => font != null && font.name.IndexOf("Bender", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        _tooltipSectionStyle = CreateTooltipStyle(21, TextAnchor.MiddleLeft);
        _tooltipBodyStyle = CreateTooltipStyle(15, TextAnchor.MiddleCenter);
        _tooltipCountStyle = CreateTooltipStyle(21, TextAnchor.MiddleCenter);
        _tooltipTimerStyle = CreateTooltipStyle(20, TextAnchor.MiddleCenter);
        _tooltipProgressStyle = CreateTooltipStyle(25, TextAnchor.MiddleCenter);
    }

    private static GUIStyle CreateTooltipStyle(int fontSize, TextAnchor alignment)
    {
        return new GUIStyle(GUI.skin.label)
        {
            font = _tooltipFont != null ? _tooltipFont : GUI.skin.font,
            fontSize = fontSize,
            alignment = alignment,
            clipping = TextClipping.Clip,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
    }

    private static void DrawOutlinedLabel(Rect rect, string text, GUIStyle style)
    {
        Color previousColor = GUI.color;
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), text, style);
        GUI.color = Color.white;
        GUI.Label(rect, text, style);
        GUI.color = previousColor;
    }

    private static void UpdateUpgradeRequirements(IEnumerable<Requirement> requirements)
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return;
        }

        HideoutRepresentation representation = Singleton<HideoutRepresentation>.Instance;
        var areasByType = new Dictionary<EAreaType, AreaData>();
        foreach (AreaData area in representation.AreaDatas)
        {
            if (area != null)
            {
                areasByType[area.Template.Type] = area;
            }
        }

        FieldInfo inventoryControllerField = AccessTools.Field(typeof(HideoutRepresentation), "_inventoryController");
        InventoryController inventoryController = inventoryControllerField?.GetValue(representation) as InventoryController;
        IEnumerable<Profile.TraderInfo> traders = inventoryController?.Profile?.TradersInfo?.Values;

        foreach (ItemRequirement requirement in requirements.OfType<ItemRequirement>())
        {
            requirement.Test(representation.AllStashItems);
        }

        foreach (Requirement requirement in requirements)
        {
            if (requirement is AreaRequirement areaRequirement)
            {
                areaRequirement.Test(areasByType);
            }
            else if (requirement is TraderRequirement traderRequirement && traders != null)
            {
                traderRequirement.Test(traders);
            }
        }
    }

    private static void DrawItemIcon(Rect rect, Item item)
    {
        if (item == null)
        {
            return;
        }

        string cacheKey = item.TemplateId.ToString();
        if (!ItemIcons.TryGetValue(cacheKey, out ItemIcon icon))
        {
            icon = ItemViewFactory.LoadItemIcon(item);
            ItemIcons.Add(cacheKey, icon);
        }

        DrawSprite(rect, icon?.Sprite, 1.12f);
    }

    private static void DrawSprite(Rect rect, Sprite sprite, float zoom)
    {
        if (sprite != null && sprite.texture != null)
        {
            Rect textureCoordinates = new Rect(
                sprite.textureRect.x / sprite.texture.width,
                sprite.textureRect.y / sprite.texture.height,
                sprite.textureRect.width / sprite.texture.width,
                sprite.textureRect.height / sprite.texture.height);
            float zoomedWidth = textureCoordinates.width / zoom;
            float zoomedHeight = textureCoordinates.height / zoom;
            textureCoordinates.x += (textureCoordinates.width - zoomedWidth) / 2f;
            textureCoordinates.y += (textureCoordinates.height - zoomedHeight) / 2f;
            textureCoordinates.width = zoomedWidth;
            textureCoordinates.height = zoomedHeight;
            float aspectRatio = sprite.textureRect.width / sprite.textureRect.height;
            Rect iconRect = rect;
            if (aspectRatio > 1f)
            {
                iconRect.height /= aspectRatio;
                iconRect.y += (rect.height - iconRect.height) / 2f;
            }
            else
            {
                iconRect.width *= aspectRatio;
                iconRect.x += (rect.width - iconRect.width) / 2f;
            }

            GUI.DrawTextureWithTexCoords(iconRect, sprite.texture, textureCoordinates);
        }
    }

    private static Sprite GetTraderAvatar(Profile.TraderInfo trader)
    {
        if (trader?.Settings == null)
        {
            return null;
        }

        string traderId = trader.Id;
        if (!TraderAvatarTasks.TryGetValue(traderId, out Task<Sprite> avatarTask))
        {
            avatarTask = trader.Settings.GetAvatar();
            if (avatarTask == null)
            {
                if (TraderAvatarWarnings.Add(traderId))
                {
                    _instance.Logger.LogWarning($"The trader avatar loader returned no task for '{traderId}'.");
                }

                return null;
            }

            TraderAvatarTasks.Add(traderId, avatarTask);
        }

        if (avatarTask.IsFaulted)
        {
            if (TraderAvatarWarnings.Add(traderId))
            {
                _instance.Logger.LogWarning($"Could not load the trader avatar for '{traderId}': {avatarTask.Exception?.GetBaseException().Message}");
            }

            return null;
        }

        if (avatarTask.IsCanceled)
        {
            if (TraderAvatarWarnings.Add(traderId))
            {
                _instance.Logger.LogWarning($"Loading the trader avatar for '{traderId}' was cancelled.");
            }

            return null;
        }

        return avatarTask.IsCompletedSuccessfully ? avatarTask.Result : null;
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static ActiveCraft GetActiveCraft(AreaData areaData)
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return null;
        }

        ProductionController productionController = Singleton<HideoutRepresentation>.Instance.ProductionController;
        if (productionController == null)
        {
            return null;
        }

        List<ItemsProducerBase> producers = productionController.GetAreaProducers(areaData);
        if (producers == null)
        {
            return null;
        }

        ActiveCraft completedCraft = null;
        ProducingItemController activeItem = null;
        ItemsProducerBase activeProducer = null;
        foreach (ItemsProducerBase producer in producers)
        {
            if (producer == null)
            {
                continue;
            }

            if (completedCraft == null && producer.HasCompletedItems && producer.CompleteItemsStorage != null)
            {
                (string SchemeId, List<Item> Items) completedItems = producer.CompleteItemsStorage.FindCompleteItems();
                if (!string.IsNullOrEmpty(completedItems.SchemeId) &&
                    completedItems.Items != null &&
                    completedItems.Items.Count > 0)
                {
                    completedCraft = CreateCraft(
                        areaData,
                        producer,
                        completedItems.SchemeId,
                        completedItems.Items[0],
                        null,
                        true);
                }
            }

            ProducingItemController candidate = producer.ClosestToEndProducingItem;
            if (candidate != null && (activeItem == null || candidate.EstimatedTimeLeft < activeItem.EstimatedTimeLeft))
            {
                activeItem = candidate;
                activeProducer = producer;
            }
        }

        if (completedCraft != null)
        {
            return completedCraft;
        }

        if (activeItem == null)
        {
            return null;
        }

        return CreateCraft(areaData, activeProducer, activeItem.SchemeId, null, activeItem, false);
    }

    private static ActiveCraft CreateCraft(
        AreaData areaData,
        ItemsProducerBase producer,
        string schemeId,
        Item completedOutput,
        ProducingItemController producingItem,
        bool isCompleted)
    {
        if (producer.Schemes == null ||
            !producer.Schemes.TryGetValue(schemeId, out BaseHideoutScheme scheme))
        {
            if (MissingRecipeWarnings.Add(schemeId))
            {
                _instance.Logger.LogWarning($"The hideout craft recipe '{schemeId}' could not be found.");
            }

            return null;
        }

        if (scheme is not ProductionScheme productionScheme)
        {
            return null;
        }

        string outputName = (productionScheme.endProduct.ToString() + " ShortName").Localized();
        Item outputItem = completedOutput;
        if (outputItem == null)
        {
            string outputItemKey = $"{areaData.Template.Type}:{schemeId}";
            if (!RecipeOutputItems.TryGetValue(outputItemKey, out outputItem) &&
                Singleton<ItemFactory>.Instantiated)
            {
                outputItem = Singleton<ItemFactory>.Instance.CreateItem(
                    MongoID.Generate(),
                    productionScheme.endProduct,
                    null);
                RecipeOutputItems.Add(outputItemKey, outputItem);
            }
        }

        return new ActiveCraft(
            outputName,
            outputItem,
            (scheme.requirements ?? Array.Empty<Requirement>()).OfType<ItemRequirement>().ToList(),
            isCompleted ? 1f : (float)producingItem.Progress,
            isCompleted ? 0f : producingItem.EstimatedTimeLeft,
            producer.IsWorking,
            isCompleted);
    }

    private static void OnBottomBarSettingChanged(object sender, EventArgs args)
    {
        HideoutScreenOverlay overlay = UnityEngine.Object.FindObjectOfType<HideoutScreenOverlay>();
        if (overlay != null)
        {
            ApplyBottomBarVisibility(overlay);
        }
    }

    private static void ApplyBottomBarVisibility(HideoutScreenOverlay overlay)
    {
        AreasPanel areasPanel = overlay._areasPanel;
        if (areasPanel == null)
        {
            return;
        }

        if (_configuredAreasPanel != areasPanel)
        {
            _configuredAreasPanel = areasPanel;
            _areasPanelCanvasGroup = areasPanel.GetComponent<CanvasGroup>();
            if (_areasPanelCanvasGroup == null)
            {
                _areasPanelCanvasGroup = areasPanel.gameObject.AddComponent<CanvasGroup>();
            }

            _areasPanelOriginalAlpha = _areasPanelCanvasGroup.alpha;
            _areasPanelOriginalBlocksRaycasts = _areasPanelCanvasGroup.blocksRaycasts;
            _areasPanelOriginalInteractable = _areasPanelCanvasGroup.interactable;
        }

        if (_hideBottomScrollbar.Value)
        {
            _areasPanelCanvasGroup.alpha = 0f;
            _areasPanelCanvasGroup.blocksRaycasts = false;
            _areasPanelCanvasGroup.interactable = false;
            return;
        }

        RestoreBottomBarVisibility();
    }

    private static void RestoreBottomBarVisibility()
    {
        if (_areasPanelCanvasGroup == null)
        {
            return;
        }

        _areasPanelCanvasGroup.alpha = _areasPanelOriginalAlpha;
        _areasPanelCanvasGroup.blocksRaycasts = _areasPanelOriginalBlocksRaycasts;
        _areasPanelCanvasGroup.interactable = _areasPanelOriginalInteractable;
    }
}
