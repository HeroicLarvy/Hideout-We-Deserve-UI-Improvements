using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HideoutWeDeserveUIImprovements;

// Per-station craft folders: a fixed bar above the craft list, drag/right-click organising,
// and a craft list that only builds the views of the selected folder.
public sealed partial class HideoutWeDeserveUIImprovementsPlugin
{
    private sealed class CraftFolder
    {
        public string Name;
        public Color Color;
        public readonly List<string> Schemes = new List<string>();
    }

    private enum FolderPopup
    {
        None,
        FolderMenu,
        CraftMenu,
        Rename
    }

    private const int MaxCraftFolders = 5;
    private const int MaxFolderNameLength = 14;
    private const float FolderBarHeight = 64f;
    private const float FolderDragThreshold = 10f;
    private const float BarTopNudge = 2f;
    private static readonly Dictionary<RectTransform, Vector2> ReservedViewports =
        new Dictionary<RectTransform, Vector2>();
    private const float PopupRowHeight = 28f;
    private const float MinPopupWidth = 190f;
    private static float PopupWidth = MinPopupWidth;
    private const string FolderNameControl = "HWD_FolderName";

    private static readonly Color[] FolderPalette =
    {
        new Color(0.91f, 0.30f, 0.24f, 1f),
        new Color(0.95f, 0.61f, 0.07f, 1f),
        new Color(0.95f, 0.83f, 0.20f, 1f),
        new Color(0.27f, 0.82f, 0.49f, 1f),
        new Color(0.20f, 0.74f, 0.80f, 1f),
        new Color(0.29f, 0.64f, 1f, 1f),
        new Color(0.66f, 0.45f, 0.93f, 1f),
        new Color(0.80f, 0.82f, 0.85f, 1f)
    };

    private static readonly Dictionary<EAreaType, List<CraftFolder>> CraftFolders =
        new Dictionary<EAreaType, List<CraftFolder>>();
    private static readonly Dictionary<EAreaType, int> SelectedCraftFolder = new Dictionary<EAreaType, int>();
    private static readonly HashSet<ProductionPanel> CraftFolderPanels = new HashSet<ProductionPanel>();

    private static readonly FieldInfo PanelViewListField = AccessTools.Field(typeof(ProductionPanel), "_viewList");
    private static readonly FieldInfo PanelViewsField = AccessTools.Field(typeof(ProductionPanel), "_views");
    private static readonly FieldInfo PanelProducersField = AccessTools.Field(typeof(ProductionPanel), "_producers");
    private static readonly FieldInfo PanelSchemesField = AccessTools.Field(typeof(ProductionPanel), "_schemes");

    private static string _loadedFoldersProfileId;
    private static FolderPopup _folderPopup;
    private static Vector2 _popupPosition;
    private static int _popupFolderIndex;
    private static string _popupSchemeId;
    private static string _renameText;
    private static bool _renameNeedsFocus;
    private static string _dragCandidateSchemeId;
    private static string _dragSchemeName;
    private static Vector2 _dragStart;
    private static bool _isDraggingCraft;
    private static bool _isDraggingFolder;
    private static int _dragFolderIndex = -1;
    private const string UnassignedLabel = "Unassigned";
    private const string AllLabel = "All";
    // Tab/selection index meaning "show every craft"; -1 is Unassigned and 0..n-1 are folders.
    private const int AllTab = -2;
    private static GUIStyle _folderCenterStyle;
    private static GUIStyle _folderNameStyle;

    private static string GetFoldersFilePath(string profileId)
    {
        return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, $"HideoutWeDeserveUIImprovements.folders.{profileId}.txt");
    }

    // Folders are stored beside the BepInEx config, never in the game profile.
    private static void EnsureFoldersLoaded()
    {
        string profileId = GetCurrentProfileId();
        if (string.IsNullOrEmpty(profileId) || profileId == _loadedFoldersProfileId)
        {
            return;
        }

        _loadedFoldersProfileId = profileId;
        CraftFolders.Clear();
        SelectedCraftFolder.Clear();
        string path = GetFoldersFilePath(profileId);
        if (!System.IO.File.Exists(path))
        {
            return;
        }

        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            string[] parts = line.Split('|');
            if (parts.Length < 5 ||
                parts[0] != "F" ||
                !Enum.TryParse(parts[1], out EAreaType area) ||
                !ColorUtility.TryParseHtmlString("#" + parts[3], out Color color))
            {
                continue;
            }

            List<CraftFolder> folders = GetFolders(area);
            if (folders.Count >= MaxCraftFolders)
            {
                continue;
            }

            var folder = new CraftFolder { Name = Uri.UnescapeDataString(parts[2]), Color = color };
            folder.Schemes.AddRange(parts[4].Split(',').Where(id => id.Length > 0));
            folders.Add(folder);
        }
    }

    private static void SaveFolders()
    {
        if (string.IsNullOrEmpty(_loadedFoldersProfileId))
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllLines(
                GetFoldersFilePath(_loadedFoldersProfileId),
                CraftFolders.SelectMany(pair => pair.Value.Select(folder =>
                    $"F|{pair.Key}|{Uri.EscapeDataString(folder.Name)}|{ColorUtility.ToHtmlStringRGB(folder.Color)}|{string.Join(",", folder.Schemes)}")));
        }
        catch (Exception exception)
        {
            _instance.Logger.LogWarning($"Could not save the craft folders: {exception.Message}");
        }
    }

    // Shrinks the scroll viewport from the top so the crafts start below the bar and are clipped
    // (and unclickable) beneath it instead of sliding under it.
    private static void ReserveFolderBarSpace(ProductionPanel panel, Rect bar)
    {
        ScrollRect scroll = panel.GetComponentInParent<ScrollRect>();
        RectTransform viewport = scroll == null
            ? null
            : scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
        if (viewport == null || ReservedViewports.ContainsKey(viewport) || !TryGetScreenRect(viewport, out Rect viewportRect))
        {
            return;
        }

        float neededPixels = bar.yMax - viewportRect.y;
        if (neededPixels <= 0f)
        {
            return;
        }

        float units = neededPixels / Mathf.Max(0.01f, viewport.lossyScale.y);
        ReservedViewports[viewport] = viewport.offsetMax;
        viewport.offsetMax = new Vector2(viewport.offsetMax.x, viewport.offsetMax.y - units);
    }

    private static void RestoreFolderBarSpace()
    {
        foreach (KeyValuePair<RectTransform, Vector2> pair in ReservedViewports)
        {
            if (pair.Key != null)
            {
                pair.Key.offsetMax = pair.Value;
            }
        }

        ReservedViewports.Clear();
    }

    private static List<CraftFolder> GetFolders(EAreaType area)
    {
        if (!CraftFolders.TryGetValue(area, out List<CraftFolder> folders))
        {
            folders = new List<CraftFolder>();
            CraftFolders[area] = folders;
        }

        return folders;
    }

    private static int GetSelectedFolderIndex(EAreaType area, List<CraftFolder> folders)
    {
        if (!SelectedCraftFolder.TryGetValue(area, out int index))
        {
            // Nothing picked yet: default to the leftmost folder.
            return folders.Count > 0 ? 0 : -1;
        }

        return index >= 0 && index < folders.Count ? index : index == AllTab ? AllTab : -1;
    }

    private static EAreaType? GetPanelArea(ProductionPanel panel)
    {
        return panel?.AreaData?.Template?.Type;
    }

    private static void RegisterCraftFolderPanel(ProductionPanel __instance)
    {
        EnsureFoldersLoaded();
        CraftFolderPanels.Add(__instance);
    }

    private static void UnregisterCraftFolderPanel(ProductionPanel __instance)
    {
        CraftFolderPanels.Remove(__instance);
        RestoreFolderBarSpace();
        RestoreHostedViews(_ => true);
    }

    // Both the first build and later re-sorts go through GetSortedSchemes, so filtering here means
    // views outside the selected folder are never created.
    private static void FilterSchemesByFolder(ProductionPanel __instance, ref IEnumerable<ProductionScheme> __result)
    {
        EAreaType? area = GetPanelArea(__instance);
        if (!_enableCraftFolders.Value || area == null || __result == null)
        {
            return;
        }

        EnsureFoldersLoaded();
        List<CraftFolder> folders = GetFolders(area.Value);
        int selected = GetSelectedFolderIndex(area.Value, folders);
        if (selected == AllTab)
        {
            return;
        }

        // Crafts in production are always kept so their views can be shown in the status bar.
        if (selected < 0)
        {
            // "Unassigned" only lists crafts that aren't filed in a folder.
            __result = __result.Where(scheme => IsSchemeProducing(area.Value, scheme._id) || !folders.Any(folder => folder.Schemes.Contains(scheme._id))).ToList();
            return;
        }

        List<string> allowed = folders[selected].Schemes;
        __result = __result.Where(scheme => IsSchemeProducing(area.Value, scheme._id) || allowed.Contains(scheme._id)).ToList();
    }

    private static AsyncViewList<ProductionScheme, ProduceView> GetCraftViewList(ProductionPanel panel)
    {
        return PanelViewListField?.GetValue(panel) as AsyncViewList<ProductionScheme, ProduceView>;
    }

    private static async void RebuildCraftList(ProductionPanel panel)
    {
        try
        {
            AsyncViewList<ProductionScheme, ProduceView> oldList = GetCraftViewList(panel);
            if (panel == null || oldList == null)
            {
                return;
            }

            if (panel._searchInputField != null)
            {
                panel._searchInputField.text = string.Empty;
            }

            // The panel's own update loops wait for _views to be non-null, so clear it while the list is rebuilt.
            PanelViewsField.SetValue(panel, null);
            oldList.Dispose();

            var producers = PanelProducersField.GetValue(panel) as List<ItemsProducerBase>;
            ItemsProducerBase producer = producers?.FirstOrDefault(candidate => candidate is ItemsProducer);
            var schemes = PanelSchemesField.GetValue(panel) as BaseHideoutScheme[];
            if (producer == null || schemes == null)
            {
                return;
            }

            ProduceView template = panel.RelatedData.Type == EPanelType.Decryption
                ? panel._decryptionProductionView
                : panel._produceViewTemplate;
            await panel.ShowProduceView(ItemUiContext.Instance, template, schemes.Length > 1, producer);
            panel.UpdateViews();
        }
        catch (Exception exception)
        {
            _instance.Logger.LogWarning($"Could not rebuild the craft list: {exception.Message}");
        }
    }

    private static void SelectCraftFolder(ProductionPanel panel, EAreaType area, int index)
    {
        List<CraftFolder> folders = GetFolders(area);
        if (GetSelectedFolderIndex(area, folders) == index)
        {
            return;
        }

        SelectedCraftFolder[area] = index;
        RebuildCraftList(panel);
    }

    private static void AssignCraftToFolder(ProductionPanel panel, EAreaType area, string schemeId, int folderIndex)
    {
        List<CraftFolder> folders = GetFolders(area);
        foreach (CraftFolder folder in folders)
        {
            folder.Schemes.Remove(schemeId);
        }

        if (folderIndex >= 0 && folderIndex < folders.Count)
        {
            folders[folderIndex].Schemes.Add(schemeId);
        }

        SaveFolders();
        RebuildCraftList(panel);
    }

    private static void MoveCraftFolder(EAreaType area, List<CraftFolder> folders, int from, int to)
    {
        int selected = GetSelectedFolderIndex(area, folders);
        CraftFolder selectedFolder = selected >= 0 ? folders[selected] : null;
        CraftFolder moved = folders[from];
        folders.RemoveAt(from);
        folders.Insert(to, moved);
        if (selectedFolder != null)
        {
            SelectedCraftFolder[area] = folders.IndexOf(selectedFolder);
        }

        SaveFolders();
    }

    private static void AddCraftFolder(ProductionPanel panel, EAreaType area)
    {
        List<CraftFolder> folders = GetFolders(area);
        if (folders.Count >= MaxCraftFolders)
        {
            return;
        }

        folders.Add(new CraftFolder
        {
            Name = $"Folder {folders.Count + 1}",
            Color = FolderPalette[folders.Count % FolderPalette.Length]
        });
        SaveFolders();
        if (folders.Count == 1 && !SelectedCraftFolder.ContainsKey(area))
        {
            // The first folder becomes the default selection, so the list must follow.
            RebuildCraftList(panel);
        }

        OpenRenamePopup(folders.Count - 1, new Vector2(Event.current.mousePosition.x, Event.current.mousePosition.y));
    }

    private static void DeleteCraftFolder(ProductionPanel panel, EAreaType area, int index)
    {
        List<CraftFolder> folders = GetFolders(area);
        if (index < 0 || index >= folders.Count)
        {
            return;
        }

        folders.RemoveAt(index);
        SelectedCraftFolder.Remove(area);
        SaveFolders();
        RebuildCraftList(panel);
    }

    private static void OpenRenamePopup(int folderIndex, Vector2 position)
    {
        _folderPopup = FolderPopup.Rename;
        _popupFolderIndex = folderIndex;
        _popupPosition = position;
        _renameText = string.Empty;
        _renameNeedsFocus = true;
    }

    private static void ClosePopup()
    {
        _folderPopup = FolderPopup.None;
        _popupSchemeId = null;
    }

    private static void ResetFolderUi()
    {
        ClosePopup();
        RestoreHostedViews(_ => true);
        _dragCandidateSchemeId = null;
        _isDraggingCraft = false;
        _isDraggingFolder = false;
        _dragFolderIndex = -1;
    }

    private static bool TryGetScreenRect(Component component, out Rect rect)
    {
        rect = default;
        if (component == null || component.transform is not RectTransform transform)
        {
            return false;
        }

        Canvas canvas = transform.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var corners = new Vector3[4];
        transform.GetWorldCorners(corners);
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        rect = Rect.MinMaxRect(bottomLeft.x, Screen.height - topRight.y, topRight.x, Screen.height - bottomLeft.y);
        return rect.width > 1f && rect.height > 1f;
    }

    private static string GetSchemeDisplayName(ProductionScheme scheme)
    {
        return (scheme.endProduct.ToString() + " ShortName").Localized();
    }

    private static bool TryGetCraftUnderMouse(
        AsyncViewList<ProductionScheme, ProduceView> viewList,
        Rect window,
        Vector2 mouse,
        out ProductionScheme scheme)
    {
        scheme = null;
        if (!window.Contains(mouse))
        {
            return false;
        }

        foreach (KeyValuePair<ProductionScheme, ProduceView> pair in viewList.Views)
        {
            if (pair.Value != null &&
                pair.Value.gameObject.activeInHierarchy &&
                TryGetScreenRect(pair.Value, out Rect rect) &&
                rect.Contains(mouse))
            {
                scheme = pair.Key;
                return true;
            }
        }

        return false;
    }

    private static void DrawFolderIcon(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color * 0.8f;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * 0.45f, rect.height * 0.25f), Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height * 0.18f, rect.width, rect.height * 0.82f), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static void FillRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static bool IsMouseDown(int button)
    {
        return Event.current.type == EventType.MouseDown && Event.current.button == button;
    }

    private static void DrawCraftFolderBar()
    {
        CraftFolderPanels.RemoveWhere(panel => panel == null);
        ProductionPanel panel = CraftFolderPanels.FirstOrDefault(candidate =>
            candidate.isActiveAndEnabled && GetCraftViewList(candidate) != null);
        EAreaType? areaOrNull = GetPanelArea(panel);
        if (!_enableCraftFolders.Value && !_enableCraftQueueBar.Value)
        {
            ResetFolderUi();
            RestoreFolderBarSpace();
            return;
        }

        if (panel == null || areaOrNull == null || !TryGetCraftWindowRect(panel, out Rect window))
        {
            ResetFolderUi();
            return;
        }

        EAreaType area = areaOrNull.Value;
        EnsureTooltipStyles();
        if (_folderNameStyle == null || _folderNameStyle.font != _raidItemTooltipBodyStyle.font)
        {
            _folderNameStyle = new GUIStyle(_raidItemTooltipBodyStyle) { fontSize = 20, wordWrap = false };
            _folderCenterStyle = new GUIStyle(_folderNameStyle) { alignment = TextAnchor.MiddleCenter };
        }

        Event current = Event.current;
        Vector2 mouse = current.mousePosition;
        List<CraftFolder> folders = GetFolders(area);
        int selected = GetSelectedFolderIndex(area, folders);
        bool popupOpen = _folderPopup != FolderPopup.None;
        bool repaint = current.type == EventType.Repaint;
        AsyncViewList<ProductionScheme, ProduceView> viewList = GetCraftViewList(panel);

        // The bar sits flush with the top edge of the station window, level with its header row.
        bool foldersEnabled = _enableCraftFolders.Value;
        bool statusEnabled = _enableCraftQueueBar.Value;
        float barHeight = foldersEnabled ? FolderBarHeight : 0f;
        float statusHeight = statusEnabled ? StatusBarHeight : 0f;
        var bar = new Rect(window.x, window.y - BarTopNudge, window.width, barHeight);
        var statusBar = new Rect(bar.x, bar.yMax, bar.width, statusHeight);
        ReserveFolderBarSpace(panel, new Rect(bar.x, bar.y, bar.width, barHeight + statusHeight));
        if (!foldersEnabled)
        {
            ClosePopup();
            _dragCandidateSchemeId = null;
            _isDraggingCraft = false;
            _isDraggingFolder = false;
            _dragFolderIndex = -1;
            DrawCraftStatusBar(statusBar, area, true, viewList, panel);
            return;
        }

        if (repaint)
        {
            FillRect(bar, new Color(0.035f, 0.045f, 0.055f, 1f));
            DrawBorder(bar, new Color(0.68f, 0.78f, 0.86f, 0.82f), 1.5f);
        }

        if (statusEnabled)
        {
            DrawCraftStatusBar(statusBar, area, !popupOpen, viewList, panel);
        }

        // Index -1 is the "Unassigned" tab (far right), 0..n-1 are folders, n is the add button.
        var tabs = new List<(int Index, Rect Rect)>();
        float x = bar.x + 6f;
        float tabY = bar.y + 6f;
        float tabHeight = bar.height - 12f;
        const float tabGap = 6f;
        const float addWidth = 46f;
        float unassignedWidth = 34f + _folderNameStyle.CalcSize(new GUIContent(UnassignedLabel)).x;
        float allWidth = 34f + _folderNameStyle.CalcSize(new GUIContent(AllLabel)).x;
        var folderWidths = new float[folders.Count];
        for (int i = 0; i < folders.Count; i++)
        {
            // Icon + padding around the measured name so the whole name always fits.
            folderWidths[i] = Mathf.Max(90f, 62f + _folderNameStyle.CalcSize(new GUIContent(folders[i].Name)).x);
        }

        float totalWidth = 12f + unassignedWidth + allWidth + addWidth + tabGap * (folders.Count + 3) + folderWidths.Sum();
        float shrink = totalWidth > bar.width && folderWidths.Length > 0
            ? Mathf.Max(0.5f, 1f - (totalWidth - bar.width) / folderWidths.Sum())
            : 1f;
        for (int i = 0; i < folders.Count; i++)
        {
            float width = folderWidths[i] * shrink;
            tabs.Add((i, new Rect(x, tabY, width, tabHeight)));
            x += width + tabGap;
        }

        if (folders.Count < MaxCraftFolders)
        {
            tabs.Add((folders.Count, new Rect(x, tabY, addWidth, tabHeight)));
        }

        float unassignedX = bar.xMax - 6f - unassignedWidth;
        tabs.Add((-1, new Rect(unassignedX, tabY, unassignedWidth, tabHeight)));
        tabs.Add((AllTab, new Rect(unassignedX - tabGap - allWidth, tabY, allWidth, tabHeight)));

        int hoveredTab = int.MinValue;
        foreach ((int index, Rect rect) in tabs)
        {
            if (rect.Contains(mouse))
            {
                hoveredTab = index;
            }
        }

        foreach ((int index, Rect rect) in tabs)
        {
            bool isAdd = index == folders.Count;
            bool isUnassigned = index == -1;
            bool hovered = !popupOpen && hoveredTab == index;
            if (repaint)
            {
                bool active = index == selected;
                bool highlighted = hovered ||
                    (_isDraggingCraft && hoveredTab == index && !isAdd && index != AllTab) ||
                    (_isDraggingFolder && hoveredTab == index && index >= 0 && !isAdd && index != _dragFolderIndex);
                FillRect(rect, active
                    ? new Color(0.30f, 0.38f, 0.45f, 0.95f)
                    : highlighted
                        ? new Color(0.22f, 0.29f, 0.35f, 0.95f)
                        : new Color(0.12f, 0.16f, 0.2f, 0.95f));
                if (index >= 0 && !isAdd)
                {
                    DrawFolderIcon(new Rect(rect.x + 8f, rect.y + 8f, 34f, rect.height - 16f), folders[index].Color);
                    DrawOutlinedLabel(new Rect(rect.x + 50f, rect.y, rect.width - 54f, rect.height), folders[index].Name, _folderNameStyle);
                    if (active)
                    {
                        FillRect(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), folders[index].Color);
                    }
                }
                else
                {
                    string label = isAdd ? "+" : isUnassigned ? UnassignedLabel : AllLabel;
                    DrawOutlinedLabel(rect, label, _folderCenterStyle);
                    if (active)
                    {
                        FillRect(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), Color.white);
                    }
                }

                DrawBorder(rect, new Color(0.68f, 0.78f, 0.86f, 0.5f), 1f);
            }
        }

        List<(ProductionScheme Scheme, Rect Rect)> craftButtons = GetCraftFolderButtons(viewList, window, statusBar);
        if (repaint)
        {
            foreach ((ProductionScheme scheme, Rect rect) in craftButtons)
            {
                bool inFolder = folders.Any(folder => folder.Schemes.Contains(scheme._id));
                bool hovered = !popupOpen && rect.Contains(mouse);
                FillRect(rect, hovered ? new Color(0.30f, 0.38f, 0.45f, 0.98f) : new Color(0.12f, 0.16f, 0.2f, 0.95f));
                Color iconColor = inFolder
                    ? folders.First(folder => folder.Schemes.Contains(scheme._id)).Color
                    : new Color(0.8f, 0.82f, 0.85f, 1f);
                DrawFolderIcon(new Rect(rect.x + 5f, rect.y + 6f, rect.width - 10f, rect.height - 12f), iconColor);
                DrawBorder(rect, new Color(0.68f, 0.78f, 0.86f, 0.6f), 1f);
            }
        }

        if (repaint && _isDraggingFolder && _dragFolderIndex >= 0 && _dragFolderIndex < folders.Count)
        {
            string name = folders[_dragFolderIndex].Name;
            var ghost = new Rect(mouse.x + 14f, mouse.y + 8f, _folderNameStyle.CalcSize(new GUIContent(name)).x + 44f, 30f);
            FillRect(ghost, new Color(0.1f, 0.13f, 0.17f, 0.92f));
            DrawBorder(ghost, new Color(0.68f, 0.78f, 0.86f, 0.9f), 1f);
            DrawFolderIcon(new Rect(ghost.x + 6f, ghost.y + 6f, 24f, ghost.height - 12f), folders[_dragFolderIndex].Color);
            DrawOutlinedLabel(new Rect(ghost.x + 36f, ghost.y, ghost.width - 40f, ghost.height), name, _folderNameStyle);
        }

        if (repaint && _isDraggingCraft)
        {
            var ghost = new Rect(mouse.x + 14f, mouse.y + 8f, 190f, 26f);
            FillRect(ghost, new Color(0.1f, 0.13f, 0.17f, 0.92f));
            DrawBorder(ghost, new Color(0.68f, 0.78f, 0.86f, 0.9f), 1f);
            DrawOutlinedLabel(ghost, _dragSchemeName, _tooltipBodyStyle);
        }

        if (!popupOpen)
        {
            HandleFolderBarInput(panel, area, viewList, window, folders, hoveredTab, mouse, craftButtons);
        }
        else
        {
            DrawFolderPopup(panel, area, folders);
        }
    }

    // A folder button sits just left of the favorite / shopping cart buttons of each visible craft.
    private static List<(ProductionScheme Scheme, Rect Rect)> GetCraftFolderButtons(
        AsyncViewList<ProductionScheme, ProduceView> viewList,
        Rect window,
        Rect bar)
    {
        var buttons = new List<(ProductionScheme, Rect)>();
        foreach (KeyValuePair<ProductionScheme, ProduceView> pair in viewList.Views)
        {
            ProduceView view = pair.Value;
            if (view == null || !view.gameObject.activeInHierarchy)
            {
                continue;
            }

            bool hasFavorite = view._favoriteButton != null && view._favoriteButton.gameObject.activeInHierarchy &&
                TryGetScreenRect(view._favoriteButton, out Rect favoriteRect);
            bool hasBuy = view._buyButton != null && view._buyButton.gameObject.activeInHierarchy &&
                TryGetScreenRect(view._buyButton, out Rect buyRect);
            Rect reference = default;
            if (hasFavorite && TryGetScreenRect(view._favoriteButton, out Rect fav))
            {
                reference = fav;
            }

            if (hasBuy && TryGetScreenRect(view._buyButton, out Rect buy))
            {
                reference = reference.width > 0f && reference.x < buy.x ? reference : buy;
            }

            if (reference.width <= 0f)
            {
                continue;
            }

            float size = Mathf.Max(24f, reference.height);
            var rect = new Rect(reference.x - size - 6f, reference.y + (reference.height - size) / 2f, size, size);
            // Clip to the scroll viewport so buttons never draw over the footer or the bars.
            ScrollRect scroll = view.GetComponentInParent<ScrollRect>();
            RectTransform viewport = scroll == null ? null : scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
            Rect clip = window;
            if (viewport != null && TryGetScreenRect(viewport, out Rect viewportRect))
            {
                clip = Rect.MinMaxRect(
                    Mathf.Max(window.xMin, viewportRect.xMin),
                    Mathf.Max(window.yMin, viewportRect.yMin),
                    Mathf.Min(window.xMax, viewportRect.xMax),
                    Mathf.Min(window.yMax, viewportRect.yMax));
            }

            if (rect.y >= bar.yMax && rect.y >= clip.yMin && rect.yMax <= clip.yMax && rect.x >= clip.xMin)
            {
                buttons.Add((pair.Key, rect));
            }
        }

        return buttons;
    }

    private static void HandleFolderBarInput(
        ProductionPanel panel,
        EAreaType area,
        AsyncViewList<ProductionScheme, ProduceView> viewList,
        Rect window,
        List<CraftFolder> folders,
        int hoveredTab,
        Vector2 mouse,
        List<(ProductionScheme Scheme, Rect Rect)> craftButtons)
    {
        Event current = Event.current;

        // Drags are tracked by polling the mouse button: IMGUI drag/up events are not reliably delivered here.
        if (_dragFolderIndex >= 0 || _dragCandidateSchemeId != null)
        {
            if (Input.GetMouseButton(0))
            {
                if ((mouse - _dragStart).magnitude > FolderDragThreshold)
                {
                    _isDraggingFolder = _dragFolderIndex >= 0;
                    _isDraggingCraft = _dragCandidateSchemeId != null;
                }
            }
            else
            {
                if (_isDraggingFolder && hoveredTab >= 0 && hoveredTab < folders.Count && hoveredTab != _dragFolderIndex)
                {
                    MoveCraftFolder(area, folders, _dragFolderIndex, hoveredTab);
                }
                else if (_isDraggingCraft && (hoveredTab == -1 || (hoveredTab >= 0 && hoveredTab < folders.Count)))
                {
                    AssignCraftToFolder(panel, area, _dragCandidateSchemeId, hoveredTab);
                }

                _isDraggingCraft = false;
                _isDraggingFolder = false;
                _dragCandidateSchemeId = null;
                _dragFolderIndex = -1;
            }
        }

        switch (current.type)
        {
            case EventType.MouseDown when current.button == 0:
                if (hoveredTab == AllTab || hoveredTab == -1 || (hoveredTab >= 0 && hoveredTab < folders.Count))
                {
                    SelectCraftFolder(panel, area, hoveredTab);
                    if (hoveredTab >= 0)
                    {
                        _dragFolderIndex = hoveredTab;
                        _dragStart = mouse;
                    }

                    current.Use();
                }
                else if (hoveredTab == folders.Count)
                {
                    AddCraftFolder(panel, area);
                    current.Use();
                }
                else if (craftButtons.FirstOrDefault(button => button.Rect.Contains(mouse)).Scheme is ProductionScheme clicked)
                {
                    _folderPopup = FolderPopup.CraftMenu;
                    _popupSchemeId = clicked._id;
                    _popupPosition = mouse;
                    current.Use();
                }
                else if (TryGetCraftUnderMouse(viewList, window, mouse, out ProductionScheme candidate))
                {
                    _dragCandidateSchemeId = candidate._id;
                    _dragSchemeName = GetSchemeDisplayName(candidate);
                    _dragStart = mouse;
                }

                break;
            case EventType.MouseDown when current.button == 1:
                if (hoveredTab >= 0 && hoveredTab < folders.Count)
                {
                    _folderPopup = FolderPopup.FolderMenu;
                    _popupFolderIndex = hoveredTab;
                    _popupPosition = mouse;
                    current.Use();
                }
                break;
        }
    }

    private static bool DrawPopupRow(Rect rect, string text, Color? swatch = null)
    {
        Event current = Event.current;
        bool hovered = rect.Contains(current.mousePosition);
        if (current.type == EventType.Repaint)
        {
            FillRect(rect, hovered ? new Color(0.30f, 0.38f, 0.45f, 0.98f) : new Color(0.12f, 0.16f, 0.2f, 0.98f));
            float textX = rect.x;
            if (swatch.HasValue)
            {
                FillRect(new Rect(rect.x + 6f, rect.y + 7f, 14f, rect.height - 14f), swatch.Value);
                textX += 24f;
            }

            DrawOutlinedLabel(new Rect(textX, rect.y, rect.xMax - textX, rect.height), text, _folderNameStyle);
        }

        if (hovered && IsMouseDown(0))
        {
            current.Use();
            return true;
        }

        return false;
    }

    // Sizes the popup to its widest row so no text is clipped.
    private static float MeasurePopupWidth(List<CraftFolder> folders)
    {
        float widest = 0f;
        void Measure(string text, float extra)
        {
            widest = Mathf.Max(widest, _folderNameStyle.CalcSize(new GUIContent(text)).x + extra);
        }

        switch (_folderPopup)
        {
            case FolderPopup.FolderMenu:
                Measure("Rename", 20f);
                Measure("Delete folder", 20f);
                break;
            case FolderPopup.CraftMenu:
                Measure("Remove from folder", 20f);
                Measure("Create a folder first", 20f);
                foreach (CraftFolder folder in folders)
                {
                    Measure(folder.Schemes.Contains(_popupSchemeId) ? $"{folder.Name} (current)" : folder.Name, 44f);
                }

                break;
        }

        return Mathf.Max(MinPopupWidth, widest + 8f);
    }

    private static void DrawFolderPopup(ProductionPanel panel, EAreaType area, List<CraftFolder> folders)
    {
        Event current = Event.current;
        if (_folderPopup == FolderPopup.FolderMenu &&
            (_popupFolderIndex < 0 || _popupFolderIndex >= folders.Count))
        {
            ClosePopup();
            return;
        }

        int rows;
        switch (_folderPopup)
        {
            case FolderPopup.FolderMenu:
                rows = 3;
                break;
            case FolderPopup.CraftMenu:
                rows = Math.Max(1, folders.Count) + 1;
                break;
            default:
                rows = 3;
                break;
        }

        float height = rows * PopupRowHeight + 8f;
        PopupWidth = MeasurePopupWidth(folders);
        var popup = new Rect(
            Mathf.Clamp(_popupPosition.x, 0f, Screen.width - PopupWidth - 4f),
            Mathf.Clamp(_popupPosition.y, 0f, Screen.height - height - 4f),
            PopupWidth,
            height);
        if (current.type == EventType.Repaint)
        {
            DrawSectionPanel(popup);
            FillRect(popup, new Color(0.08f, 0.1f, 0.13f, 0.98f));
            DrawBorder(popup, new Color(0.68f, 0.78f, 0.86f, 0.82f), 1.5f);
        }

        if ((current.type == EventType.MouseDown) && !popup.Contains(current.mousePosition))
        {
            if (_folderPopup == FolderPopup.Rename)
            {
                CommitRename(folders);
            }

            ClosePopup();
            current.Use();
            return;
        }

        float y = popup.y + 4f;
        switch (_folderPopup)
        {
            case FolderPopup.FolderMenu:
                if (DrawPopupRow(new Rect(popup.x + 4f, y, PopupWidth - 8f, PopupRowHeight), "Rename"))
                {
                    OpenRenamePopup(_popupFolderIndex, _popupPosition);
                    _renameText = folders[_popupFolderIndex].Name;
                    return;
                }

                y += PopupRowHeight;
                if (DrawPopupRow(new Rect(popup.x + 4f, y, PopupWidth - 8f, PopupRowHeight), "Delete folder"))
                {
                    DeleteCraftFolder(panel, area, _popupFolderIndex);
                    ClosePopup();
                    return;
                }

                y += PopupRowHeight;
                float swatchSize = (PopupWidth - 8f) / FolderPalette.Length;
                for (int i = 0; i < FolderPalette.Length; i++)
                {
                    var swatchRect = new Rect(popup.x + 4f + swatchSize * i, y + 2f, swatchSize - 2f, PopupRowHeight - 4f);
                    bool swatchHovered = swatchRect.Contains(current.mousePosition);
                    if (current.type == EventType.Repaint)
                    {
                        FillRect(swatchRect, FolderPalette[i]);
                        if (swatchHovered || folders[_popupFolderIndex].Color == FolderPalette[i])
                        {
                            DrawBorder(swatchRect, Color.white, 2f);
                        }
                    }

                    if (swatchHovered && IsMouseDown(0))
                    {
                        folders[_popupFolderIndex].Color = FolderPalette[i];
                        SaveFolders();
                        current.Use();
                        ClosePopup();
                        return;
                    }
                }

                break;
            case FolderPopup.CraftMenu:
                if (folders.Count == 0)
                {
                    DrawOutlinedLabel(new Rect(popup.x + 8f, y, PopupWidth - 16f, PopupRowHeight), "Create a folder first", _folderNameStyle);
                    y += PopupRowHeight;
                }

                for (int i = 0; i < folders.Count; i++)
                {
                    string label = folders[i].Schemes.Contains(_popupSchemeId) ? $"{folders[i].Name} (current)" : folders[i].Name;
                    if (DrawPopupRow(new Rect(popup.x + 4f, y, PopupWidth - 8f, PopupRowHeight), label, folders[i].Color))
                    {
                        AssignCraftToFolder(panel, area, _popupSchemeId, i);
                        ClosePopup();
                        return;
                    }

                    y += PopupRowHeight;
                }

                if (DrawPopupRow(new Rect(popup.x + 4f, y, PopupWidth - 8f, PopupRowHeight), "Remove from folder"))
                {
                    AssignCraftToFolder(panel, area, _popupSchemeId, -1);
                    ClosePopup();
                }

                break;
            case FolderPopup.Rename:
                DrawRenamePopup(popup, folders);
                break;
        }
    }

    private static void CommitRename(List<CraftFolder> folders)
    {
        string name = (_renameText ?? string.Empty).Trim();
        if (name.Length > 0 && _popupFolderIndex >= 0 && _popupFolderIndex < folders.Count)
        {
            folders[_popupFolderIndex].Name = name;
            SaveFolders();
        }
    }

    private static void DrawRenamePopup(Rect popup, List<CraftFolder> folders)
    {
        Event current = Event.current;
        if (_popupFolderIndex < 0 || _popupFolderIndex >= folders.Count)
        {
            ClosePopup();
            return;
        }

        if (_renameText.Length == 0 && _renameNeedsFocus)
        {
            _renameText = folders[_popupFolderIndex].Name;
        }

        if (current.type == EventType.KeyDown)
        {
            if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            {
                CommitRename(folders);
                ClosePopup();
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                ClosePopup();
                current.Use();
                return;
            }
        }

        DrawOutlinedLabel(new Rect(popup.x + 8f, popup.y + 4f, popup.width - 16f, PopupRowHeight), "Folder name", _folderNameStyle);
        GUI.SetNextControlName(FolderNameControl);
        _renameText = GUI.TextField(
            new Rect(popup.x + 8f, popup.y + 4f + PopupRowHeight, popup.width - 16f, PopupRowHeight - 2f),
            _renameText,
            MaxFolderNameLength);
        if (_renameNeedsFocus)
        {
            GUI.FocusControl(FolderNameControl);
            _renameNeedsFocus = false;
        }

        if (DrawPopupRow(new Rect(popup.x + 4f, popup.y + 4f + PopupRowHeight * 2f, popup.width - 8f, PopupRowHeight), "OK"))
        {
            CommitRename(folders);
            ClosePopup();
        }
    }
}
