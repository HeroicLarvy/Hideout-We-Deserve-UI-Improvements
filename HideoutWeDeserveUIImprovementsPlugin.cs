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

public sealed class ConfigurationManagerAttributes
{
    public int? Order;
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
public sealed class HideoutWeDeserveUIImprovementsPlugin : BaseUnityPlugin
{
    private enum ItemMarkerCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private const string PluginId = "com.heroiclarvy.hideoutwedeserveuiimprovements";
    private const string PluginName = "Hideout We Deserve - UI Improvements";
    private const string PluginVersion = "1.1.5";
    private const float HoverItemGap = 4.5f;
    private const float HoverProgressWidth = 104f;
    private const float RaidItemMarkerSize = 18f;
    private const string RaidItemMarkerLayerName = "HideoutUpgradeMarkerLayer";
    private static readonly Type WishlistGridViewType =
        AccessTools.TypeByName("UI.DragAndDrop.ItemViews.WishlistGridView");

    private static HideoutWeDeserveUIImprovementsPlugin _instance;
    private static ConfigEntry<bool> _hideBottomScrollbar;
    private static ConfigEntry<bool> _showUpgradeRequirementsOnHover;
    private static ConfigEntry<bool> _showActiveCraftOnHover;
    private static ConfigEntry<bool> _showItemNamesOnHover;
    private static ConfigEntry<bool> _openStationMenuWithoutZoom;
    private static ConfigEntry<bool> _showCurrentUpgradeItemMarkers;
    private static ConfigEntry<bool> _showFutureUpgradeItemMarkers;
    private static ConfigEntry<bool> _showFavoriteCraftItemMarkers;
    private static ConfigEntry<bool> _showUpgradeItemMarkersInStash;
    private static ConfigEntry<bool> _showUpgradeItemTooltipsOnHover;
    private static ConfigEntry<Color> _currentUpgradeItemMarkerColor;
    private static ConfigEntry<Color> _futureUpgradeItemMarkerColor;
    private static ConfigEntry<Color> _favoriteCraftItemMarkerColor;
    private static ConfigEntry<ItemMarkerCorner> _itemMarkerCorner;
    private static ConfigEntry<bool> _showCurrencyItemMarkers;
    private static ConfigEntry<bool> _showRoubleItemMarkers;
    private static ConfigEntry<bool> _showDollarItemMarkers;
    private static ConfigEntry<bool> _showEuroItemMarkers;
    private static AreaWorldPanel _hoveredWorldPanel;
    private static ItemView _hoveredRaidItemView;
    private static HideoutRepresentation _hideoutRepresentationForMarkers;
    private static AreasPanel _configuredAreasPanel;
    private static CanvasGroup _areasPanelCanvasGroup;
    private static float _areasPanelOriginalAlpha;
    private static bool _areasPanelOriginalBlocksRaycasts;
    private static bool _areasPanelOriginalInteractable;
    private static float _nextRequirementRefreshAt;
    private static float _upgradeItemNeedsRefreshAt;
    private static float _nextUpgradeItemNeedsLoadRetryAt;
    private static bool _upgradeItemNeedsDirty;
    private static bool _hasLoadedUpgradeItemNeeds;
    private static readonly Dictionary<string, ItemIcon> ItemIcons = new Dictionary<string, ItemIcon>();
    private static readonly Dictionary<string, Item> RecipeOutputItems = new Dictionary<string, Item>();
    private static readonly Dictionary<string, List<UpgradeItemNeed>> CurrentUpgradeItemNeeds =
        new Dictionary<string, List<UpgradeItemNeed>>();
    private static readonly Dictionary<string, List<UpgradeItemNeed>> FutureUpgradeItemNeeds =
        new Dictionary<string, List<UpgradeItemNeed>>();
    private static readonly Dictionary<string, List<FavoriteCraftItemNeed>> FavoriteCraftItemNeeds =
        new Dictionary<string, List<FavoriteCraftItemNeed>>();
    private static readonly HashSet<ItemView> RaidItemViews = new HashSet<ItemView>();
    private static readonly HashSet<UpgradeMarkerHoverHandler> RaidItemMarkerHandlers =
        new HashSet<UpgradeMarkerHoverHandler>();
    private static readonly Dictionary<string, Task<Sprite>> TraderAvatarTasks = new Dictionary<string, Task<Sprite>>();
    private static readonly HashSet<string> TraderAvatarWarnings = new HashSet<string>();
    private static readonly HashSet<string> MissingRecipeWarnings = new HashSet<string>();
    private static readonly Dictionary<int, Sprite> UpgradeMarkerSprites = new Dictionary<int, Sprite>();
    private const string RoubleTemplateId = "5449016a4bdc2d6f028b456f";
    private const string DollarTemplateId = "5696686a4bdc2da3298b456a";
    private const string EuroTemplateId = "569668774bdc2da2298b4568";
    private static Vector2 _raidItemTooltipScrollPosition;
    private static string _raidItemTooltipTemplateId;
    private static GUISkin _tooltipStyleSkin;
    private static Font _tooltipFont;
    private static GUIStyle _tooltipSectionStyle;
    private static GUIStyle _tooltipBodyStyle;
    private static GUIStyle _raidItemTooltipBodyStyle;
    private static GUIStyle _tooltipCountStyle;
    private static GUIStyle _tooltipMoneyCountStyle;
    private static readonly HashSet<string> MoneyTemplateIds = new HashSet<string>
    {
        "5449016a4bdc2d6f028b456f", // Roubles
        "5696686a4bdc2d9d6c8b4567", // Dollars
        "569668774bdc2da2298b4568"  // Euros
    };
    private static GUIStyle _tooltipTimerStyle;
    private static GUIStyle _tooltipProgressStyle;
    private Harmony _harmony;

    private sealed class FavoriteCraftItemNeed
    {
        public readonly string OutputName;
        public readonly int OwnedCount;
        public readonly int NeededCount;

        public FavoriteCraftItemNeed(string outputName, int ownedCount, int neededCount)
        {
            OutputName = outputName;
            OwnedCount = ownedCount;
            NeededCount = neededCount;
        }
    }

    private sealed class PendingFavoriteCraftItemNeed
    {
        public readonly string TemplateId;
        public readonly string OutputName;
        public readonly ItemRequirement Requirement;

        public PendingFavoriteCraftItemNeed(string templateId, string outputName, ItemRequirement requirement)
        {
            TemplateId = templateId;
            OutputName = outputName;
            Requirement = requirement;
        }
    }

    private sealed class UpgradeItemNeed
    {
        public readonly string AreaName;
        public readonly string StageName;
        public readonly int OwnedCount;
        public readonly int NeededCount;

        public UpgradeItemNeed(string areaName, string stageName, int ownedCount, int neededCount)
        {
            AreaName = areaName;
            StageName = stageName;
            OwnedCount = ownedCount;
            NeededCount = neededCount;
        }
    }

    internal sealed class UpgradeMarkerHoverHandler :
        MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler
    {
        private ItemView _itemView;
        private bool _markerActive;

        internal void Initialize(ItemView itemView, bool markerActive = true)
        {
            _itemView = itemView;
            _markerActive = markerActive;
        }

        internal Image MarkerImage => GetComponent<Image>();

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_markerActive)
            {
                HideoutWeDeserveUIImprovementsPlugin.OnRaidItemMarkerPointerEntered(_itemView);
            }
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (_markerActive)
            {
                HideoutWeDeserveUIImprovementsPlugin.OnRaidItemMarkerPointerExited(_itemView);
            }
        }

        private void OnDestroy()
        {
            RaidItemMarkerHandlers.Remove(this);
        }
    }

    private sealed class PendingUpgradeItemNeed
    {
        public readonly string TemplateId;
        public readonly string AreaName;
        public readonly string StageName;
        public readonly ItemRequirement Requirement;
        public readonly bool IsFuture;

        public PendingUpgradeItemNeed(
            string templateId,
            string areaName,
            string stageName,
            ItemRequirement requirement,
            bool isFuture)
        {
            TemplateId = templateId;
            AreaName = areaName;
            StageName = stageName;
            Requirement = requirement;
            IsFuture = isFuture;
        }
    }

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
        public readonly int StageLevel;
        public readonly List<KeyValuePair<int, List<ItemRequirement>>> FutureStages;

        public DefectiveWallRequirements(
            bool floorMopped,
            ItemRequirement mopItem,
            List<ItemRequirement> dismantleItems,
            int stageLevel,
            List<KeyValuePair<int, List<ItemRequirement>>> futureStages)
        {
            FloorMopped = floorMopped;
            MopItem = mopItem;
            DismantleItems = dismantleItems;
            StageLevel = stageLevel;
            FutureStages = futureStages;
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
        _showCurrentUpgradeItemMarkers = Config.Bind(
            "Item Markers",
            "ShowCurrentUpgradeRequirements",
            true,
            "Mark raid items still needed for each station's next construction or upgrade.");
        _currentUpgradeItemMarkerColor = Config.Bind(
            "Item Markers",
            "CurrentUpgradeMarkerColor",
            new Color(68f / 255f, 210f / 255f, 125f / 255f, 1f),
            "Color used for items needed by the next hideout construction or upgrade.");
        _showFutureUpgradeItemMarkers = Config.Bind(
            "Item Markers",
            "ShowFutureUpgradeRequirements",
            true,
            "Mark raid items still needed for later hideout upgrade stages.");
        _futureUpgradeItemMarkerColor = Config.Bind(
            "Item Markers",
            "FutureUpgradeMarkerColor",
            new Color(75f / 255f, 164f / 255f, 1f, 1f),
            "Color used for items needed by later hideout upgrade stages.");
        _showFavoriteCraftItemMarkers = Config.Bind(
            "Item Markers",
            "ShowFavoriteCraftIngredients",
            true,
            "Mark ingredients needed by hideout crafts you have favorited.");
        _favoriteCraftItemMarkerColor = Config.Bind(
            "Item Markers",
            "FavoriteCraftMarkerColor",
            new Color(1f, 0.78f, 0.22f, 1f),
            "Color used for items needed by favorited hideout crafts.");
        _itemMarkerCorner = Config.Bind(
            "Item Markers",
            "MarkerCorner",
            ItemMarkerCorner.BottomLeft,
            "Corner of an item icon where item markers are shown.");
        _showUpgradeItemMarkersInStash = Config.Bind(
            "Item Markers",
            "ShowUpgradeMarkersInStash",
            true,
            "Show item markers in stash and hideout item grids.");
        _showUpgradeItemTooltipsOnHover = Config.Bind(
            "Item Markers",
            "ShowUpgradeTooltipsOnHover",
            true,
            "Show item details when hovering a marker.");
        _showCurrencyItemMarkers = Config.Bind(
            "Item Markers",
            "ShowCurrencyMarkers",
            false,
            new ConfigDescription(
                "Allow currency items to receive normal hideout or favorite-craft markers.",
                tags: new object[] { new ConfigurationManagerAttributes { Order = 50 } }));
        _showRoubleItemMarkers = Config.Bind(
            "Item Markers",
            "ShowRoubleMarkers",
            true,
            new ConfigDescription(
                "Allow roubles to show a marker when required by an enabled marker type.",
                tags: new object[] { new ConfigurationManagerAttributes { Order = 51 } }));
        _showDollarItemMarkers = Config.Bind(
            "Item Markers",
            "ShowDollarMarkers",
            true,
            new ConfigDescription(
                "Allow dollars to show a marker when required by an enabled marker type.",
                tags: new object[] { new ConfigurationManagerAttributes { Order = 52 } }));
        _showEuroItemMarkers = Config.Bind(
            "Item Markers",
            "ShowEuroMarkers",
            true,
            new ConfigDescription(
                "Allow euros to show a marker when required by an enabled marker type.",
                tags: new object[] { new ConfigurationManagerAttributes { Order = 53 } }));
        _showCurrentUpgradeItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showFutureUpgradeItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showFavoriteCraftItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showCurrencyItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showRoubleItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showDollarItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showEuroItemMarkers.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _itemMarkerCorner.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _showUpgradeItemMarkersInStash.SettingChanged += OnUpgradeMarkerVisibilityChanged;
        _currentUpgradeItemMarkerColor.SettingChanged += OnUpgradeMarkerColorChanged;
        _futureUpgradeItemMarkerColor.SettingChanged += OnUpgradeMarkerColorChanged;
        _favoriteCraftItemMarkerColor.SettingChanged += OnUpgradeMarkerColorChanged;
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
        _harmony.Patch(
            AccessTools.PropertySetter(typeof(ItemView), nameof(ItemView.Item)),
            postfix: CreateLastPostfix(nameof(OnItemViewItemChanged)));
        _harmony.Patch(
            AccessTools.PropertySetter(typeof(ItemView), nameof(ItemView.IsSearched)),
            postfix: CreateLastPostfix(nameof(OnItemViewSearchStateChanged)));
        _harmony.Patch(
            AccessTools.Method(typeof(GridItemView), nameof(GridItemView.NewGridItemView)),
            postfix: CreateLastPostfix(nameof(OnRaidItemViewCreated)));
        _harmony.Patch(
            AccessTools.Method(WishlistGridViewType, "Show"),
            postfix: CreateLastPostfix(nameof(AfterWishlistViewShown)));
        _harmony.Patch(
            AccessTools.Method(typeof(GridView), "AddItemView"),
            postfix: CreateLastPostfix(nameof(OnRaidGridItemViewAdded)));
        var swapEventsPostfix =
            new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnRaidItemSwapEventsRaised))
            {
                priority = Priority.Last
            };
        _harmony.Patch(
            AccessTools.Method(typeof(SwapResult), nameof(SwapResult.RaiseEvents)),
            postfix: swapEventsPostfix);
        PatchGridItemLifecycle("EFT.IRefreshHandler.OnRefreshItem", nameof(OnRaidItemViewRefreshed));
        PatchGridItemLifecycle("EFT.IAddHandler.OnItemAdded", nameof(OnRaidItemViewRefreshed));
        PatchGridItemLifecycle("EFT.IRemoveHandler.OnItemRemoved", nameof(OnRaidItemViewUnbound));
        PatchGridItemLifecycle("EFT.IBindHandler.OnBindItem", nameof(OnRaidItemViewRefreshed));
        PatchGridItemLifecycle("EFT.IUnbindHandler.OnUnbindItem", nameof(OnRaidItemViewUnbound));
        PatchQuickSlotItemViewLifecycle("EFT.IRefreshHandler.OnRefreshItem", nameof(OnRaidItemViewRefreshed));
        PatchQuickSlotItemViewLifecycle("EFT.IAddHandler.OnItemAdded", nameof(OnRaidItemViewRefreshed));
        PatchQuickSlotItemViewLifecycle("EFT.IRemoveHandler.OnItemRemoved", nameof(OnRaidItemViewUnbound));
        PatchQuickSlotItemViewLifecycle("RegisterItemView", nameof(OnItemViewInitialized));
        _harmony.Patch(
            AccessTools.Method(typeof(SlotView), "CreateItemView"),
            postfix: CreateLastPostfix(nameof(OnRaidSlotItemViewCreated)));
        _harmony.Patch(
            AccessTools.Method(typeof(GridItemView), nameof(GridItemView.ShowTooltip)),
            prefix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(ShouldShowDefaultItemTooltip)));
        _harmony.Patch(
            AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.AddFavoriteScheme)),
            postfix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnFavoriteProductionSchemesChanged)));
        _harmony.Patch(
            AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.RemoveFavoriteScheme)),
            postfix: new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), nameof(OnFavoriteProductionSchemesChanged)));

        Logger.LogInfo($"{PluginName} loaded.");
    }

    private void PatchGridItemLifecycle(string methodName, string callbackName)
    {
        MethodInfo method = typeof(GridItemView).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            Logger.LogWarning($"Could not find GridItemView lifecycle callback '{methodName}'; item markers may not refresh for that event.");
            return;
        }

        _harmony.Patch(method, postfix: CreateLastPostfix(callbackName));
    }

    private void PatchQuickSlotItemViewLifecycle(string methodName, string callbackName)
    {
        MethodInfo method = typeof(QuickSlotItemView).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
            (methodName == "RegisterItemView" ? BindingFlags.DeclaredOnly : 0));
        if (method == null)
        {
            Logger.LogWarning($"Could not find QuickSlotItemView lifecycle callback '{methodName}'; quick-slot item markers may not refresh for that event.");
            return;
        }

        _harmony.Patch(method, postfix: CreateLastPostfix(callbackName));
    }

    private static HarmonyMethod CreateLastPostfix(string callbackName)
    {
        return new HarmonyMethod(typeof(HideoutWeDeserveUIImprovementsPlugin), callbackName)
        {
            priority = Priority.Last
        };
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _hideBottomScrollbar.SettingChanged -= OnBottomBarSettingChanged;
        _showCurrentUpgradeItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showFutureUpgradeItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showFavoriteCraftItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showCurrencyItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showRoubleItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showDollarItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showEuroItemMarkers.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _itemMarkerCorner.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _showUpgradeItemMarkersInStash.SettingChanged -= OnUpgradeMarkerVisibilityChanged;
        _currentUpgradeItemMarkerColor.SettingChanged -= OnUpgradeMarkerColorChanged;
        _futureUpgradeItemMarkerColor.SettingChanged -= OnUpgradeMarkerColorChanged;
        _favoriteCraftItemMarkerColor.SettingChanged -= OnUpgradeMarkerColorChanged;
        if (_hideoutRepresentationForMarkers != null)
        {
            _hideoutRepresentationForMarkers.OnItemsChanged -= OnHideoutItemsChanged;
            _hideoutRepresentationForMarkers.OnItemChanged -= OnHideoutItemChanged;
            _hideoutRepresentationForMarkers = null;
        }
        if (_areasPanelCanvasGroup != null)
        {
            RestoreBottomBarVisibility();
        }

        _instance = null;
        _hoveredWorldPanel = null;
        _hoveredRaidItemView = null;
        _upgradeItemNeedsDirty = false;
        _hasLoadedUpgradeItemNeeds = false;
        ItemIcons.Clear();
        RecipeOutputItems.Clear();
        CurrentUpgradeItemNeeds.Clear();
        FutureUpgradeItemNeeds.Clear();
        FavoriteCraftItemNeeds.Clear();
        foreach (UpgradeMarkerHoverHandler markerHandler in RaidItemMarkerHandlers.ToArray())
        {
            if (markerHandler != null)
            {
                Transform markerLayer = markerHandler.transform.parent;
                if (markerLayer != null && markerLayer.name == RaidItemMarkerLayerName)
                {
                    UnityEngine.Object.Destroy(markerLayer.gameObject);
                }
                else
                {
                    UnityEngine.Object.Destroy(markerHandler);
                }
            }
        }

        RaidItemMarkerHandlers.Clear();
        RaidItemViews.Clear();
        MissingRecipeWarnings.Clear();
        TraderAvatarTasks.Clear();
        TraderAvatarWarnings.Clear();
        DestroyUpgradeMarkerSprites();
    }

    private void Update()
    {
        if (!_hasLoadedUpgradeItemNeeds &&
            Time.realtimeSinceStartup >= _nextUpgradeItemNeedsLoadRetryAt)
        {
            EnsureUpgradeItemNeedsLoaded();
            _nextUpgradeItemNeedsLoadRetryAt = Time.realtimeSinceStartup + 1.5f;
        }

        if (_upgradeItemNeedsDirty && Time.realtimeSinceStartup >= _upgradeItemNeedsRefreshAt)
        {
            _upgradeItemNeedsDirty = false;
            RefreshUpgradeItemNeedsFromHideout();
            RefreshRaidItemMarkers();
        }
    }

    private void OnGUI()
    {
        DrawWorldPanelTooltip();
        if (_showUpgradeItemTooltipsOnHover.Value)
        {
            DrawRaidItemUpgradeTooltip();
        }
    }

    private static void OnHideoutScreenShown(HideoutScreenOverlay __instance)
    {
        ApplyBottomBarVisibility(__instance);
        RefreshUpgradeItemNeedsFromHideout();
        RefreshRaidItemMarkers();
    }

    private static void AfterWishlistViewShown(Component __instance)
    {
        GridItemView itemView = __instance != null
            ? __instance.GetComponentInParent<GridItemView>(true)
            : null;
        if (itemView == null)
        {
            return;
        }

        EnsureUpgradeItemNeedsLoaded();
        RaidItemViews.Add(itemView);
        RefreshFallbackRaidItemMarker(itemView);
    }

    private static void OnRaidItemViewCreated(GridItemView __result)
    {
        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(__result);
    }

    private static void OnRaidGridItemViewAdded(GridView __instance, ItemView __0)
    {
        if (__instance == null || __0 == null)
        {
            return;
        }

        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(__0);
    }

    private static void OnItemViewItemChanged(ItemView __instance)
    {
        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(__instance);
    }

    private static void OnItemViewSearchStateChanged(ItemView __instance)
    {
        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(__instance);
    }

    private static void OnRaidItemSwapEventsRaised(SwapResult __instance, CommandStatus __1)
    {
        if (__instance != null && __1 == CommandStatus.Succeed)
        {
            RefreshRaidItemMarkers();
        }
    }

    private static void OnItemViewInitialized(ItemView __instance)
    {
        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(__instance);
    }

    private static void OnRaidItemViewRefreshed(ItemView __instance)
    {
        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(__instance);
    }

    private static void OnRaidSlotItemViewCreated(SlotView __instance)
    {
        if (__instance == null)
        {
            return;
        }

        ItemView itemView = __instance.GetComponentInChildren<ItemView>(true);
        if (itemView == null)
        {
            return;
        }

        EnsureUpgradeItemNeedsLoaded();
        RefreshRaidItemMarker(itemView);
    }

    private static void OnRaidItemViewUnbound(ItemView __instance)
    {
        if (__instance == null)
        {
            return;
        }

        if (__instance.Item == null)
        {
            RaidItemViews.Remove(__instance);
        }
        else
        {
            RaidItemViews.Add(__instance);
        }

        if (_hoveredRaidItemView == __instance)
        {
            _hoveredRaidItemView = null;
            _raidItemTooltipTemplateId = null;
        }
    }

    private static bool ShouldShowDefaultItemTooltip(ItemView __instance)
    {
        return !_showUpgradeItemTooltipsOnHover.Value || _hoveredRaidItemView != __instance;
    }

    internal static void OnRaidItemMarkerPointerEntered(ItemView itemView)
    {
        if (!_showUpgradeItemTooltipsOnHover.Value || !HasDetailedItemTooltip(itemView))
        {
            return;
        }

        _hoveredRaidItemView = itemView;
        _raidItemTooltipTemplateId = null;
        if (itemView is GridItemView gridItemView)
        {
            gridItemView.HideTooltip();
        }
    }

    private static bool HasDetailedItemTooltip(ItemView itemView)
    {
        string templateId = itemView?.Item?.TemplateId.ToString();
        if (string.IsNullOrEmpty(templateId))
        {
            return false;
        }

        return (_showCurrentUpgradeItemMarkers.Value && CurrentUpgradeItemNeeds.ContainsKey(templateId)) ||
            (_showFutureUpgradeItemMarkers.Value && FutureUpgradeItemNeeds.ContainsKey(templateId)) ||
            (_showFavoriteCraftItemMarkers.Value && FavoriteCraftItemNeeds.ContainsKey(templateId));
    }

    internal static void OnRaidItemMarkerPointerExited(ItemView itemView)
    {
        if (_hoveredRaidItemView == itemView)
        {
            _hoveredRaidItemView = null;
            _raidItemTooltipTemplateId = null;
        }
    }

    private static void EnsureUpgradeItemNeedsLoaded()
    {
        if (!_hasLoadedUpgradeItemNeeds && Singleton<HideoutRepresentation>.Instantiated)
        {
            RefreshUpgradeItemNeedsFromHideout();
            RefreshRaidItemMarkers();
        }
    }

    private static void OnHideoutItemsChanged()
    {
        _upgradeItemNeedsDirty = true;
        _upgradeItemNeedsRefreshAt = Time.realtimeSinceStartup + 0.25f;
    }

    private static void OnHideoutItemChanged(Item item)
    {
        _upgradeItemNeedsDirty = true;
        _upgradeItemNeedsRefreshAt = Time.realtimeSinceStartup + 0.25f;
    }

    private static void OnUpgradeMarkerColorChanged(object sender, EventArgs eventArgs)
    {
        DestroyUpgradeMarkerSprites();
        RefreshRaidItemMarkers();
    }

    private static void OnUpgradeMarkerVisibilityChanged(object sender, EventArgs eventArgs)
    {
        RefreshRaidItemMarkers();
    }

    private static void OnFavoriteProductionSchemesChanged()
    {
        _upgradeItemNeedsDirty = true;
        _upgradeItemNeedsRefreshAt = Time.realtimeSinceStartup + 0.1f;
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

    private static void RefreshUpgradeItemNeedsFromHideout()
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return;
        }

        HideoutRepresentation representation = Singleton<HideoutRepresentation>.Instance;
        if (_hideoutRepresentationForMarkers != representation)
        {
            if (_hideoutRepresentationForMarkers != null)
            {
                _hideoutRepresentationForMarkers.OnItemsChanged -= OnHideoutItemsChanged;
                _hideoutRepresentationForMarkers.OnItemChanged -= OnHideoutItemChanged;
            }

            _hideoutRepresentationForMarkers = representation;
            _hideoutRepresentationForMarkers.OnItemsChanged += OnHideoutItemsChanged;
            _hideoutRepresentationForMarkers.OnItemChanged += OnHideoutItemChanged;
        }

        List<AreaData> areaDatas = representation.AreaDatas;
        if (areaDatas == null || areaDatas.Count == 0)
        {
            return;
        }

        var pendingNeeds = new List<PendingUpgradeItemNeed>();
        var pendingFavoriteCraftNeeds = new List<PendingFavoriteCraftItemNeed>();
        var requirementsToTest = new List<Requirement>();
        foreach (ProductionScheme scheme in representation.FavoriteProductionSchemes ??
            Enumerable.Empty<ProductionScheme>())
        {
            if (scheme == null)
            {
                continue;
            }

            string outputName = (scheme.endProduct.ToString() + " ShortName").Localized();
            foreach (ItemRequirement requirement in scheme.requirements?.OfType<ItemRequirement>()
                ?? Enumerable.Empty<ItemRequirement>())
            {
                if (requirement == null ||
                    string.IsNullOrEmpty(requirement.TemplateId) ||
                    requirement.IntCount <= 0)
                {
                    continue;
                }

                pendingFavoriteCraftNeeds.Add(new PendingFavoriteCraftItemNeed(
                    requirement.TemplateId,
                    outputName,
                    requirement));
                requirementsToTest.Add(requirement);
            }
        }

        foreach (AreaData areaData in areaDatas)
        {
            if (areaData?.Template == null)
            {
                continue;
            }

            if (areaData.Template.Type == EAreaType.EmergencyWall)
            {
                DefectiveWallRequirements wallRequirements = GetDefectiveWallRequirements(areaData);
                if (!wallRequirements.FloorMopped && wallRequirements.MopItem != null)
                {
                    AddPendingUpgradeItemNeed(
                        pendingNeeds,
                        requirementsToTest,
                        areaData.Template.Name,
                        "Mop the floor",
                        wallRequirements.MopItem,
                        false);
                }

                foreach (ItemRequirement dismantleItem in wallRequirements.DismantleItems)
                {
                    AddPendingUpgradeItemNeed(
                        pendingNeeds,
                        requirementsToTest,
                        areaData.Template.Name,
                        $"Stage {wallRequirements.StageLevel} dismantle",
                        dismantleItem,
                        false);
                }

                foreach (KeyValuePair<int, List<ItemRequirement>> futureStage in wallRequirements.FutureStages)
                {
                    foreach (ItemRequirement futureItem in futureStage.Value)
                    {
                        AddPendingUpgradeItemNeed(
                            pendingNeeds,
                            requirementsToTest,
                            areaData.Template.Name,
                            $"Stage {futureStage.Key} dismantle",
                            futureItem,
                            true);
                    }
                }

                continue;
            }

            if (areaData.CurrentLevel >= areaData.Template.MaxLevel ||
                !TryGetNextStage(areaData, out int nextStageLevel, out Stage nextStage))
            {
                continue;
            }

            string currentStageName = $"{areaData.CurrentLevel}\u2192{nextStageLevel}";
            foreach (ItemRequirement itemRequirement in nextStage.Requirements?.OfType<ItemRequirement>()
                ?? Enumerable.Empty<ItemRequirement>())
            {
                AddPendingUpgradeItemNeed(
                    pendingNeeds,
                    requirementsToTest,
                    areaData.Template.Name,
                    currentStageName,
                    itemRequirement,
                    false);
            }

            if (areaData.Template.Stages == null)
            {
                continue;
            }

            foreach (KeyValuePair<int, Stage> futureStage in areaData.Template.Stages
                .Where(stage => stage.Value != null && stage.Key > nextStageLevel)
                .OrderBy(stage => stage.Key))
            {
                foreach (ItemRequirement itemRequirement in futureStage.Value.Requirements?.OfType<ItemRequirement>()
                    ?? Enumerable.Empty<ItemRequirement>())
                {
                    AddPendingUpgradeItemNeed(
                        pendingNeeds,
                        requirementsToTest,
                        areaData.Template.Name,
                        $"{futureStage.Key - 1}\u2192{futureStage.Key}",
                        itemRequirement,
                        true);
                }
            }
        }

        UpdateUpgradeRequirements(requirementsToTest);
        var currentNeeds = new Dictionary<string, List<UpgradeItemNeed>>();
        var futureNeeds = new Dictionary<string, List<UpgradeItemNeed>>();
        var favoriteCraftNeeds = new Dictionary<string, List<FavoriteCraftItemNeed>>();
        foreach (PendingUpgradeItemNeed pendingNeed in pendingNeeds)
        {
            int neededCount = pendingNeed.Requirement.IntCount;
            int ownedCount = Mathf.Max(0, pendingNeed.Requirement.UserItemsCount);

            Dictionary<string, List<UpgradeItemNeed>> destination =
                pendingNeed.IsFuture ? futureNeeds : currentNeeds;
            if (!destination.TryGetValue(pendingNeed.TemplateId, out List<UpgradeItemNeed> itemNeeds))
            {
                itemNeeds = new List<UpgradeItemNeed>();
                destination.Add(pendingNeed.TemplateId, itemNeeds);
            }

            UpgradeItemNeed existingNeed = itemNeeds.FirstOrDefault(need =>
                need.AreaName == pendingNeed.AreaName && need.StageName == pendingNeed.StageName);
            if (existingNeed == null)
            {
                itemNeeds.Add(new UpgradeItemNeed(
                    pendingNeed.AreaName,
                    pendingNeed.StageName,
                    ownedCount,
                    neededCount));
            }
            else
            {
                int existingIndex = itemNeeds.IndexOf(existingNeed);
                itemNeeds[existingIndex] = new UpgradeItemNeed(
                    existingNeed.AreaName,
                    existingNeed.StageName,
                    existingNeed.OwnedCount + ownedCount,
                    existingNeed.NeededCount + neededCount);
            }
        }

        foreach (PendingFavoriteCraftItemNeed pendingNeed in pendingFavoriteCraftNeeds)
        {
            if (!favoriteCraftNeeds.TryGetValue(pendingNeed.TemplateId, out List<FavoriteCraftItemNeed> itemNeeds))
            {
                itemNeeds = new List<FavoriteCraftItemNeed>();
                favoriteCraftNeeds.Add(pendingNeed.TemplateId, itemNeeds);
            }

            itemNeeds.Add(new FavoriteCraftItemNeed(
                pendingNeed.OutputName,
                Mathf.Max(0, pendingNeed.Requirement.UserItemsCount),
                pendingNeed.Requirement.IntCount));
        }

        CurrentUpgradeItemNeeds.Clear();
        FutureUpgradeItemNeeds.Clear();
        FavoriteCraftItemNeeds.Clear();
        CopyUpgradeNeeds(currentNeeds, CurrentUpgradeItemNeeds);
        CopyUpgradeNeeds(futureNeeds, FutureUpgradeItemNeeds);
        foreach (KeyValuePair<string, List<FavoriteCraftItemNeed>> entry in favoriteCraftNeeds)
        {
            FavoriteCraftItemNeeds.Add(entry.Key, entry.Value);
        }
        _hasLoadedUpgradeItemNeeds = true;
        _upgradeItemNeedsDirty = false;
    }

    private static void AddPendingUpgradeItemNeed(
        ICollection<PendingUpgradeItemNeed> pendingNeeds,
        ICollection<Requirement> requirementsToTest,
        string areaName,
        string stageName,
        ItemRequirement requirement,
        bool isFuture)
    {
        if (requirement == null || string.IsNullOrEmpty(requirement.TemplateId) || requirement.IntCount <= 0)
        {
            return;
        }

        pendingNeeds.Add(new PendingUpgradeItemNeed(
            requirement.TemplateId,
            areaName,
            stageName,
            requirement,
            isFuture));
        requirementsToTest.Add(requirement);
    }

    private static void CopyUpgradeNeeds(
        IDictionary<string, List<UpgradeItemNeed>> source,
        IDictionary<string, List<UpgradeItemNeed>> destination)
    {
        foreach (KeyValuePair<string, List<UpgradeItemNeed>> entry in source)
        {
            destination.Add(entry.Key, entry.Value);
        }
    }

    private static bool TryGetNextStage(AreaData areaData, out int nextStageLevel, out Stage nextStage)
    {
        nextStageLevel = 0;
        nextStage = null;
        if (areaData?.Template?.Stages == null)
        {
            return false;
        }

        nextStageLevel = areaData.CurrentLevel + 1;
        return areaData.Template.Stages.TryGetValue(nextStageLevel, out nextStage) &&
            nextStage != null;
    }

    private static void RefreshRaidItemMarkers()
    {
        ItemView[] itemViews = RaidItemViews.ToArray();
        foreach (ItemView itemView in itemViews)
        {
            if (itemView == null)
            {
                RaidItemViews.Remove(itemView);
                continue;
            }

            RefreshRaidItemMarker(itemView);
        }
    }

    private static void RefreshRaidItemMarker(ItemView itemView)
    {
        if (itemView == null)
        {
            return;
        }

        if (itemView is GridItemView gridItemView)
        {
            if (gridItemView.Item == null)
            {
                RaidItemViews.Remove(itemView);
            }
            else
            {
                RaidItemViews.Add(itemView);
            }

            RefreshFallbackRaidItemMarker(gridItemView);
            return;
        }

        RefreshFallbackRaidItemMarker(itemView);
    }

    private static void RefreshFallbackRaidItemMarker(ItemView itemView)
    {
        Item item = itemView.Item;
        if (item == null)
        {
            RaidItemViews.Remove(itemView);
            UpgradeMarkerHoverHandler existingMarkerHandler = FindRaidItemMarker(itemView);
            if (existingMarkerHandler != null && existingMarkerHandler.MarkerImage != null)
            {
                existingMarkerHandler.MarkerImage.enabled = false;
                existingMarkerHandler.Initialize(itemView, false);
            }

            return;
        }

        RaidItemViews.Add(itemView);
        GetItemMarkerNeeds(
            item,
            out bool showMarkerInCurrentContext,
            out bool needsCurrent,
            out bool needsFuture,
            out bool needsFavoriteCraft);

        UpgradeMarkerHoverHandler markerHandler = FindRaidItemOverlayMarker(itemView);
        if (markerHandler == null)
        {
            if (!needsCurrent && !needsFuture && !needsFavoriteCraft)
            {
                return;
            }

            markerHandler = CreateRaidItemMarker(itemView);
            if (markerHandler == null)
            {
                return;
            }
        }
        else
        {
            markerHandler.Initialize(itemView);
        }

        Image markerImage = markerHandler.MarkerImage;
        if (markerImage == null)
        {
            return;
        }

        RectTransform markerLayer = EnsureRaidItemMarkerLayer(itemView, markerHandler);
        if (markerLayer == null)
        {
            markerImage.enabled = false;
            markerHandler.Initialize(itemView, false);
            return;
        }

        markerImage.enabled = showMarkerInCurrentContext &&
            (needsCurrent || needsFuture || needsFavoriteCraft);
        markerHandler.Initialize(itemView, markerImage.enabled);
        if (!markerImage.enabled && _hoveredRaidItemView == itemView)
        {
            _hoveredRaidItemView = null;
            _raidItemTooltipTemplateId = null;
        }

        if (markerImage.enabled)
        {
            ConfigureRaidItemMarkerCorner(markerImage.rectTransform);
            markerLayer.SetAsLastSibling();
            markerImage.transform.SetAsLastSibling();
            markerImage.sprite = GetUpgradeMarkerSprite(needsCurrent, needsFuture, needsFavoriteCraft);
        }
    }

    private static void GetItemMarkerNeeds(
        Item item,
        out bool showMarkerInCurrentContext,
        out bool needsCurrent,
        out bool needsFuture,
        out bool needsFavoriteCraft)
    {
        showMarkerInCurrentContext =
            IsInRaid() || _showUpgradeItemMarkersInStash.Value;
        string templateId = item.TemplateId.ToString();
        bool currencyAllowed = !IsCurrencyItem(templateId) || IsCurrencyItemEnabled(templateId);
        needsCurrent = showMarkerInCurrentContext &&
            _showCurrentUpgradeItemMarkers.Value &&
            currencyAllowed &&
            !string.IsNullOrEmpty(templateId) &&
            CurrentUpgradeItemNeeds.ContainsKey(templateId);
        needsFuture = showMarkerInCurrentContext &&
            _showFutureUpgradeItemMarkers.Value &&
            currencyAllowed &&
            !string.IsNullOrEmpty(templateId) &&
            FutureUpgradeItemNeeds.ContainsKey(templateId);
        needsFavoriteCraft = showMarkerInCurrentContext &&
            _showFavoriteCraftItemMarkers.Value &&
            currencyAllowed &&
            !string.IsNullOrEmpty(templateId) &&
            FavoriteCraftItemNeeds.ContainsKey(templateId);
    }

    private static bool IsCurrencyItem(string templateId)
    {
        return templateId == RoubleTemplateId ||
            templateId == DollarTemplateId ||
            templateId == EuroTemplateId;
    }

    private static bool IsCurrencyItemEnabled(string templateId)
    {
        return _showCurrencyItemMarkers.Value &&
            ((templateId == RoubleTemplateId && _showRoubleItemMarkers.Value) ||
             (templateId == DollarTemplateId && _showDollarItemMarkers.Value) ||
             (templateId == EuroTemplateId && _showEuroItemMarkers.Value));
    }

    private static bool IsInRaid()
    {
        return Singleton<GameWorld>.Instantiated &&
            Singleton<GameWorld>.Instance.MainPlayer != null;
    }

    private static UpgradeMarkerHoverHandler FindRaidItemMarker(ItemView itemView)
    {
        return itemView.GetComponentInChildren<UpgradeMarkerHoverHandler>(true);
    }

    private static UpgradeMarkerHoverHandler FindRaidItemOverlayMarker(ItemView itemView)
    {
        UpgradeMarkerHoverHandler[] markerHandlers =
            itemView.GetComponentsInChildren<UpgradeMarkerHoverHandler>(true);
        foreach (UpgradeMarkerHoverHandler markerHandler in markerHandlers)
        {
            if (markerHandler.transform.parent != null &&
                markerHandler.transform.parent.name == RaidItemMarkerLayerName)
            {
                return markerHandler;
            }
        }

        return null;
    }

    private static UpgradeMarkerHoverHandler CreateRaidItemMarker(ItemView itemView)
    {
        var markerLayerObject = new GameObject(RaidItemMarkerLayerName, typeof(RectTransform));
        markerLayerObject.transform.SetParent(itemView.transform, false);

        var markerObject = new GameObject(
            "HideoutUpgradeMarker",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        markerObject.transform.SetParent(markerLayerObject.transform, false);

        RectTransform markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.sizeDelta = new Vector2(RaidItemMarkerSize, RaidItemMarkerSize);
        ConfigureRaidItemMarkerCorner(markerRect);
        markerRect.SetAsLastSibling();

        Image markerImage = markerObject.GetComponent<Image>();
        markerImage.raycastTarget = true;
        markerImage.enabled = false;

        UpgradeMarkerHoverHandler markerHandler = markerObject.AddComponent<UpgradeMarkerHoverHandler>();
        markerHandler.Initialize(itemView);
        RaidItemMarkerHandlers.Add(markerHandler);

        return markerHandler;
    }

    private static RectTransform EnsureRaidItemMarkerLayer(
        ItemView itemView,
        UpgradeMarkerHoverHandler markerHandler)
    {
        RectTransform markerParent = itemView.MainImage != null
            ? itemView.MainImage.rectTransform
            : itemView.transform as RectTransform;
        if (markerParent == null)
        {
            return null;
        }

        Transform markerLayer = markerHandler.transform.parent;
        if (markerLayer == null ||
            markerLayer.parent != markerParent ||
            markerLayer.name != RaidItemMarkerLayerName)
        {
            if (markerLayer != null && markerLayer.name == RaidItemMarkerLayerName)
            {
                markerLayer.SetParent(markerParent, false);
            }
            else
            {
                var markerLayerObject = new GameObject(RaidItemMarkerLayerName, typeof(RectTransform));
                markerLayer = markerLayerObject.transform;
                markerLayer.SetParent(markerParent, false);
            }

            markerHandler.transform.SetParent(markerLayer, false);
        }

        RectTransform markerLayerRect = (RectTransform)markerLayer;
        markerLayerRect.anchorMin = Vector2.zero;
        markerLayerRect.anchorMax = Vector2.one;
        markerLayerRect.localRotation = Quaternion.identity;
        markerLayerRect.localScale = Vector3.one;
        markerLayerRect.offsetMin = Vector2.zero;
        markerLayerRect.offsetMax = Vector2.zero;
        markerLayer.SetAsLastSibling();

        return markerLayerRect;
    }

    private static void ConfigureRaidItemMarkerCorner(RectTransform markerRect)
    {
        bool left = _itemMarkerCorner.Value == ItemMarkerCorner.TopLeft ||
            _itemMarkerCorner.Value == ItemMarkerCorner.BottomLeft;
        bool top = _itemMarkerCorner.Value == ItemMarkerCorner.TopLeft ||
            _itemMarkerCorner.Value == ItemMarkerCorner.TopRight;
        var anchor = new Vector2(left ? 0f : 1f, top ? 1f : 0f);

        markerRect.anchorMin = anchor;
        markerRect.anchorMax = anchor;
        markerRect.pivot = anchor;
        markerRect.anchoredPosition = new Vector2(left ? 1f : -1f, top ? -1f : 1f);
    }

    private static Sprite GetUpgradeMarkerSprite(
        bool needsCurrent,
        bool needsFuture,
        bool needsFavoriteCraft)
    {
        int markerFlags = (needsCurrent ? 1 : 0) |
            (needsFuture ? 2 : 0) |
            (needsFavoriteCraft ? 4 : 0);
        if (UpgradeMarkerSprites.TryGetValue(markerFlags, out Sprite sprite) && sprite != null)
        {
            return sprite;
        }

        var colors = new List<Color>();
        if (needsCurrent)
        {
            colors.Add(_currentUpgradeItemMarkerColor.Value);
        }

        if (needsFuture)
        {
            colors.Add(_futureUpgradeItemMarkerColor.Value);
        }

        if (needsFavoriteCraft)
        {
            colors.Add(_favoriteCraftItemMarkerColor.Value);
        }

        sprite = CreateUpgradeMarkerSprite(colors);
        UpgradeMarkerSprites[markerFlags] = sprite;
        return sprite;
    }

    private static Sprite CreateUpgradeMarkerSprite(IList<Color> colors)
    {
        const int size = 24;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "HideoutUpgradeMarkerTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (!IsHouseMarkerPixel(x, y))
                {
                    continue;
                }

                bool isOutline = false;
                for (int offsetY = -1; offsetY <= 1 && !isOutline; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (!IsHouseMarkerPixel(x + offsetX, y + offsetY))
                        {
                            isOutline = true;
                            break;
                        }
                    }
                }

                int colorIndex = Mathf.Min(x * colors.Count / size, colors.Count - 1);
                pixels[y * size + x] = isOutline
                    ? new Color32(12, 16, 20, 255)
                    : (Color32)colors[colorIndex];
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static bool IsHouseMarkerPixel(int x, int y)
    {
        if (x < 0 || x >= 24 || y < 0 || y >= 24)
        {
            return false;
        }

        bool roof = y >= 12 && y <= 22 && Mathf.Abs(x - 12) <= 22 - y;
        bool walls = x >= 5 && x <= 19 && y >= 3 && y <= 14;
        return roof || walls;
    }

    private static void DrawRaidItemUpgradeTooltip()
    {
        if (_hoveredRaidItemView == null)
        {
            return;
        }

        Item item = _hoveredRaidItemView.Item;
        string templateId = item?.TemplateId.ToString();
        if (string.IsNullOrEmpty(templateId))
        {
            return;
        }

        CurrentUpgradeItemNeeds.TryGetValue(templateId, out List<UpgradeItemNeed> currentNeeds);
        FutureUpgradeItemNeeds.TryGetValue(templateId, out List<UpgradeItemNeed> futureNeeds);
        FavoriteCraftItemNeeds.TryGetValue(templateId, out List<FavoriteCraftItemNeed> favoriteCraftNeeds);
        List<string> currentLines = _showCurrentUpgradeItemMarkers.Value && currentNeeds != null
            ? currentNeeds.Select(FormatUpgradeItemNeed).ToList()
            : null;
        List<string> futureLines = _showFutureUpgradeItemMarkers.Value && futureNeeds != null
            ? futureNeeds.Select(FormatUpgradeItemNeed).ToList()
            : null;
        List<string> favoriteCraftLines = _showFavoriteCraftItemMarkers.Value && favoriteCraftNeeds != null
            ? favoriteCraftNeeds.Select(FormatFavoriteCraftItemNeed).ToList()
            : null;
        bool showCurrent = currentLines?.Count > 0;
        bool showFuture = futureLines?.Count > 0;
        bool showFavoriteCraft = favoriteCraftLines?.Count > 0;
        if (!showCurrent && !showFuture && !showFavoriteCraft)
        {
            _raidItemTooltipTemplateId = null;
            _raidItemTooltipScrollPosition = Vector2.zero;
            return;
        }

        EnsureTooltipStyles();
        const float padding = 4f;
        const float sectionGap = 8f;
        const float rowGap = 2f;
        const float dividerHeight = 2f;
        const float bottomSectionPadding = 4f;
        const float iconSize = 20f;
        const float iconTextGap = 4f;
        float iconColumnWidth = iconSize + iconTextGap;
        if (_raidItemTooltipTemplateId != templateId)
        {
            _raidItemTooltipTemplateId = templateId;
            _raidItemTooltipScrollPosition = Vector2.zero;
        }

        float contentWidth = 0f;
        float contentHeight = 0f;
        if (showCurrent)
        {
            contentWidth = MeasureTooltipSectionWidth(currentLines, iconSize, iconTextGap);
        }

        if (showFuture)
        {
            contentWidth = Mathf.Max(contentWidth, MeasureTooltipSectionWidth(futureLines, iconSize, iconTextGap));
        }

        if (showFavoriteCraft)
        {
            contentWidth = Mathf.Max(
                contentWidth,
                MeasureTooltipSectionWidth(favoriteCraftLines, iconSize, iconTextGap));
        }

        float maxContentWidth = Mathf.Max(1f, Screen.width - padding * 2f - 20f);
        contentWidth = Mathf.Min(
            contentWidth,
            maxContentWidth);

        if (showCurrent)
        {
            contentHeight += MeasureTooltipSectionHeight(
                currentLines,
                iconSize,
                iconColumnWidth,
                contentWidth,
                rowGap);
        }

        if (showFuture)
        {
            contentHeight += MeasureTooltipSectionHeight(
                futureLines,
                iconSize,
                iconColumnWidth,
                contentWidth,
                rowGap);
        }

        if (showFavoriteCraft)
        {
            contentHeight += MeasureTooltipSectionHeight(
                favoriteCraftLines,
                iconSize,
                iconColumnWidth,
                contentWidth,
                rowGap);
        }

        int sectionCount = (showCurrent ? 1 : 0) +
            (showFuture ? 1 : 0) +
            (showFavoriteCraft ? 1 : 0);
        contentHeight += Mathf.Max(0, sectionCount - 1) * (sectionGap * 2f + dividerHeight);
        contentHeight += bottomSectionPadding;

        float width = Mathf.Min(contentWidth + padding * 2f, Mathf.Max(1f, Screen.width - 20f));
        float height = contentHeight + padding * 2f;
        float visibleHeight = Mathf.Min(height, Mathf.Max(80f, Screen.height - 20f));
        Vector2 mousePosition = Input.mousePosition;
        float left = Mathf.Clamp(mousePosition.x + 18f, 0f, Screen.width - width);
        float top = Mathf.Clamp(Screen.height - mousePosition.y + 18f, 0f, Screen.height - visibleHeight);
        var viewport = new Rect(left, top, width, visibleHeight);
        DrawUpgradeTooltipBackground(viewport);
        _raidItemTooltipScrollPosition = GUI.BeginScrollView(
            viewport,
            _raidItemTooltipScrollPosition,
            new Rect(0f, 0f, width, height));

        float currentY = padding;
        bool hasPreviousSection = false;
        if (showCurrent)
        {
            if (hasPreviousSection)
            {
                currentY = DrawUpgradeTooltipDivider(padding, currentY, contentWidth, sectionGap, dividerHeight);
            }

            currentY += DrawUpgradeNeedSection(
                padding,
                currentY,
                contentWidth,
                GetUpgradeMarkerSprite(true, false, false),
                currentLines,
                iconSize,
                iconColumnWidth,
                rowGap);
            hasPreviousSection = true;
        }

        if (showFuture)
        {
            if (hasPreviousSection)
            {
                currentY = DrawUpgradeTooltipDivider(padding, currentY, contentWidth, sectionGap, dividerHeight);
            }

            currentY += DrawUpgradeNeedSection(
                padding,
                currentY,
                contentWidth,
                GetUpgradeMarkerSprite(false, true, false),
                futureLines,
                iconSize,
                iconColumnWidth,
                rowGap);
            hasPreviousSection = true;
        }

        if (showFavoriteCraft)
        {
            if (hasPreviousSection)
            {
                currentY = DrawUpgradeTooltipDivider(padding, currentY, contentWidth, sectionGap, dividerHeight);
            }

            currentY += DrawUpgradeNeedSection(
                padding,
                currentY,
                contentWidth,
                GetUpgradeMarkerSprite(false, false, true),
                favoriteCraftLines,
                iconSize,
                iconColumnWidth,
                rowGap);
        }

        GUI.EndScrollView();
        DrawUpgradeTooltipBorder(viewport);
    }

    private static void DrawUpgradeTooltipBackground(Rect rect)
    {
        Color previousColor = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static void DrawUpgradeTooltipBorder(Rect rect)
    {
        for (int offset = 5; offset >= 1; offset--)
        {
            float alpha = (6f - offset) * 0.07f;
            DrawBorder(
                new Rect(
                    rect.x - offset,
                    rect.y - offset,
                    rect.width + offset * 2f,
                    rect.height + offset * 2f),
                new Color(0f, 0f, 0f, alpha),
                1f);
        }

        DrawBorder(rect, Color.white, 1f);
    }

    private static float MeasureTooltipSectionWidth(
        IList<string> lines,
        float iconSize,
        float iconTextGap)
    {
        float width = 0f;
        for (int i = 0; i < lines.Count; i++)
        {
            float lineWidth = _raidItemTooltipBodyStyle.CalcSize(new GUIContent(lines[i])).x +
                iconSize + iconTextGap;
            width = Mathf.Max(width, lineWidth);
        }

        return width;
    }

    private static float MeasureTooltipSectionHeight(
        IList<string> lines,
        float iconSize,
        float iconColumnWidth,
        float sectionWidth,
        float rowGap)
    {
        float textWidth = Mathf.Max(1f, sectionWidth - iconColumnWidth);
        float textHeight = 0f;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                textHeight += rowGap;
            }

            textHeight += _raidItemTooltipBodyStyle.CalcHeight(
                new GUIContent(lines[i]),
                textWidth);
        }

        return Mathf.Max(iconSize, textHeight);
    }

    private static float DrawUpgradeNeedSection(
        float left,
        float top,
        float width,
        Sprite markerSprite,
        IList<string> lines,
        float iconSize,
        float iconColumnWidth,
        float rowGap)
    {
        float textWidth = Mathf.Max(1f, width - iconColumnWidth);
        float currentY = top;
        Rect iconRect = new Rect(left, currentY, iconSize, iconSize);
        if (markerSprite != null)
        {
            GUI.DrawTexture(iconRect, markerSprite.texture, ScaleMode.ScaleToFit, true);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                currentY += rowGap;
            }

            string line = lines[i];
            float lineHeight = _raidItemTooltipBodyStyle.CalcHeight(new GUIContent(line), textWidth);
            DrawOutlinedLabel(
                new Rect(left + iconColumnWidth, currentY, textWidth, lineHeight),
                line,
                _raidItemTooltipBodyStyle);
            currentY += lineHeight;
        }

        return Mathf.Max(iconSize, currentY - top);
    }

    private static string FormatUpgradeItemNeed(UpgradeItemNeed need)
    {
        return $"{need.AreaName} {need.StageName}: {need.OwnedCount}/{need.NeededCount}";
    }

    private static string FormatFavoriteCraftItemNeed(FavoriteCraftItemNeed need)
    {
        return $"{need.OutputName}: {need.OwnedCount}/{need.NeededCount}";
    }

    private static float DrawUpgradeTooltipDivider(float left, float top, float width, float gap, float dividerHeight)
    {
        float dividerY = top + gap;
        DrawBorder(
            new Rect(left, dividerY, width, dividerHeight),
            new Color(0.7f, 0.7f, 0.7f, 0.9f),
            dividerHeight);
        return dividerY + dividerHeight + gap;
    }

    private static void DestroyUpgradeMarkerSprites()
    {
        foreach (Sprite sprite in UpgradeMarkerSprites.Values)
        {
            if (sprite != null)
            {
                Texture texture = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        UpgradeMarkerSprites.Clear();
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
        (string Heading, string Text)? buildProgress = _showUpgradeRequirementsOnHover.Value
            ? GetBuildProgress(areaData)
            : null;
        if (_showUpgradeRequirementsOnHover.Value && !buildProgress.HasValue)
        {
            if (areaData.Template.Type == EAreaType.EmergencyWall)
            {
                wallRequirements = GetDefectiveWallRequirements(areaData);
            }
            else
            {
                if (TryGetNextStage(areaData, out _, out Stage nextStage))
                {
                    upgradeRequirements.AddRange(nextStage.Requirements?
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

        if (upgradeRequirements.Count == 0 && wallRequirements == null && craft == null && !buildProgress.HasValue)
        {
            return;
        }

        DrawHoverTooltip(areaData, upgradeRequirements, craft, wallRequirements, buildProgress);
    }

    private static (string Heading, string Text)? GetBuildProgress(AreaData areaData)
    {
        bool isConstruction = areaData.CurrentLevel <= 0;
        switch (areaData.Status)
        {
            case EAreaStatus.Constructing:
            case EAreaStatus.Upgrading:
            case EAreaStatus.AutoUpgrading:
                TimeSpan remaining = areaData.TimeLeft;
                string time = remaining <= TimeSpan.Zero
                    ? "Finishing..."
                    : $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                return (isConstruction ? "Constructing:" : "Upgrading:", time);
            case EAreaStatus.ReadyToInstallConstruct:
            case EAreaStatus.ReadyToInstallUpgrade:
                return (isConstruction ? "Construction Complete:" : "Upgrade Complete:", "Ready to install");
            default:
                return null;
        }
    }

    private static void DrawHoverTooltip(
        AreaData areaData,
        List<Requirement> upgradeRequirements,
        ActiveCraft craft,
        DefectiveWallRequirements wallRequirements,
        (string Heading, string Text)? buildProgress)
    {
        const float buildTextHeight = 36f;
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
        string wallStageLabel = wallRequirements == null || wallRequirements.StageLevel <= 0
            ? string.Empty
            : $"Stage {wallRequirements.StageLevel} - ";
        string wallDismantleHeading = $"{wallStageLabel}Dismantle:";
        string wallMopHeading = wallRequirements == null
            ? string.Empty
            : wallRequirements.FloorMopped
                ? $"{wallStageLabel}Mop the floor: The floor has been mopped"
                : wallRequirements.MopItem != null &&
                  wallRequirements.MopItem.UserItemsCount >= wallRequirements.MopItem.IntCount
                    ? $"{wallStageLabel}Mop the floor: Available"
                    : $"{wallStageLabel}Mop the floor:";
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

        if (buildProgress.HasValue)
        {
            contentWidth = Mathf.Max(
                contentWidth,
                MeasureTextWidth(buildProgress.Value.Heading, _tooltipSectionStyle),
                MeasureTextWidth("00:00:00", _tooltipProgressStyle),
                MeasureTextWidth(buildProgress.Value.Text, _tooltipProgressStyle));
        }

        if (wallRequirements != null)
        {
            contentWidth = Mathf.Max(contentWidth, MeasureTextWidth(wallMopHeading, _tooltipSectionStyle));
            contentWidth = Mathf.Max(contentWidth, MeasureTextWidth(wallDismantleHeading, _tooltipSectionStyle));
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

        if (buildProgress.HasValue)
        {
            height += sectionPadding * 2f + sectionHeight + sectionGap + buildTextHeight;
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
            (buildProgress.HasValue ? 1 : 0) +
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
            if (buildProgress.HasValue || upgradeRequirements.Count > 0 || wallRequirements != null)
            {
                currentY += sectionGap;
            }
        }

        if (buildProgress.HasValue)
        {
            float buildHeight = sectionPadding * 2f + sectionHeight + sectionGap + buildTextHeight;
            DrawSectionPanel(new Rect(sectionLeft, currentY, panelWidth, buildHeight));
            currentY += sectionPadding;
            DrawOutlinedLabel(
                new Rect(contentLeft, currentY, availableWidth, sectionHeight),
                buildProgress.Value.Heading,
                _tooltipSectionStyle);
            currentY += sectionHeight + sectionGap;
            DrawOutlinedLabel(
                new Rect(contentLeft, currentY, availableWidth, buildTextHeight),
                buildProgress.Value.Text,
                _tooltipProgressStyle);
            currentY += buildTextHeight + sectionPadding;
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
                    wallDismantleHeading,
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
            bool isMoney = MoneyTemplateIds.Contains(requirement.TemplateId);
            DrawItemCard(
                itemRect,
                requirement.Item,
                requirement.ItemName,
                isMoney
                    ? $"{FormatMoneyAmount(availableCount)}/{FormatMoneyAmount(requirement.IntCount)}"
                    : $"{availableCount}/{requirement.IntCount}",
                showNames,
                availableCount >= requirement.IntCount,
                isMoney);
        }

        return MeasureItemFlowHeight(requirements.Count, itemWidth, availableWidth, itemHeight);
    }

    private static string FormatMoneyAmount(int amount)
    {
        if (amount >= 1000000)
        {
            return TrimTrailingZeros(amount / 1000000d, 2) + "M";
        }

        if (amount >= 1000)
        {
            return TrimTrailingZeros(amount / 1000d, 1) + "k";
        }

        return amount.ToString();
    }

    private static string TrimTrailingZeros(double value, int decimals)
    {
        double truncated = Math.Floor(value * Math.Pow(10, decimals)) / Math.Pow(10, decimals);
        return truncated.ToString("0." + new string('#', decimals), System.Globalization.CultureInfo.InvariantCulture);
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
        if (areaData.CurrentLevel <= 0)
        {
            return "Construction Requirements";
        }

        return TryGetNextStage(areaData, out int nextStageLevel, out _)
            ? $"Level {nextStageLevel} Upgrade Requirements"
            : "Upgrade Requirements";
    }

    private static DefectiveWallRequirements GetDefectiveWallRequirements(AreaData areaData)
    {
        // Each wall stage has its own mop improvement and dismantle requirements, so follow the current level.
        int stageLevel = areaData.CurrentLevel + 1;
        Stage nextStage = null;
        bool hasNextStage = areaData.Template.Stages != null &&
            areaData.Template.Stages.TryGetValue(stageLevel, out nextStage) &&
            nextStage != null;

        Improvement mopImprovement = FindPendingImprovement(areaData.CurrentStage) ??
            (hasNextStage ? FindPendingImprovement(nextStage) : null);
        ItemRequirement mopItem = mopImprovement?.Requirements?.OfType<ItemRequirement>().FirstOrDefault();
        bool floorMopped = mopImprovement == null;

        var dismantleItems = new List<ItemRequirement>();
        if (hasNextStage)
        {
            dismantleItems.AddRange(nextStage.Requirements?.OfType<ItemRequirement>()
                ?? Enumerable.Empty<ItemRequirement>());
        }

        var futureStages = new List<KeyValuePair<int, List<ItemRequirement>>>();
        if (areaData.Template.Stages != null)
        {
            foreach (KeyValuePair<int, Stage> futureStage in areaData.Template.Stages
                .Where(stage => stage.Value != null && stage.Key > stageLevel)
                .OrderBy(stage => stage.Key))
            {
                futureStages.Add(new KeyValuePair<int, List<ItemRequirement>>(
                    futureStage.Key,
                    (futureStage.Value.Requirements?.OfType<ItemRequirement>()
                        ?? Enumerable.Empty<ItemRequirement>()).ToList()));
            }
        }

        return new DefectiveWallRequirements(floorMopped, mopItem, dismantleItems, hasNextStage ? stageLevel : 0, futureStages);
    }

    private static Improvement FindPendingImprovement(Stage stage)
    {
        return stage?.Improvements?.Data?.FirstOrDefault(improvement =>
            improvement != null && !improvement.Completed);
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
        bool? completionStatus = null,
        bool compactCount = false)
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
                    compactCount ? _tooltipMoneyCountStyle : _tooltipCountStyle);
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
        _raidItemTooltipBodyStyle = CreateTooltipStyle(18, TextAnchor.MiddleLeft);
        _tooltipCountStyle = CreateTooltipStyle(21, TextAnchor.MiddleCenter);
        _tooltipMoneyCountStyle = CreateTooltipStyle(13, TextAnchor.MiddleCenter);
        _tooltipMoneyCountStyle.wordWrap = false;
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
