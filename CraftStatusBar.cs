using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Hideout;using EFT.InventoryLogic;
using EFT.UI;using UnityEngine.UI;
using UnityEngine;

namespace HideoutWeDeserveUIImprovements;

// A second fixed bar under the folder bar: the active craft on the left, the craft queue on the right.
public sealed partial class HideoutWeDeserveUIImprovementsPlugin
{
    private const float StatusBarHeight = 150f;

    private static string GetSchemeDisplayName(EAreaType area, string schemeId)
    {
        return TryFindProducer(area, schemeId, out _, out ProductionScheme scheme)
            ? (scheme.endProduct.ToString() + " ShortName").Localized()
            : schemeId;
    }

    private sealed class HostedView
    {
        public Transform Parent;
        public int SiblingIndex;
        public Vector2 Size;
    }

    private static readonly Dictionary<ProduceView, HostedView> HostedViews = new Dictionary<ProduceView, HostedView>();
    private static RectTransform _statusHost;

    private static void RestoreHostedViews(Func<ProduceView, bool> shouldRestore)
    {
        foreach (ProduceView view in HostedViews.Keys.ToList())
        {
            if (view != null && !shouldRestore(view))
            {
                continue;
            }

            if (view != null && HostedViews[view].Parent != null)
            {
                HostedView info = HostedViews[view];
                view.transform.SetParent(info.Parent, false);
                view.transform.SetSiblingIndex(info.SiblingIndex);
                view.transform.localScale = Vector3.one;
                if (view.transform is RectTransform rect)
                {
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new Vector2(0f, info.Size.y);
                }

                LayoutRebuilder.MarkLayoutForRebuild(info.Parent as RectTransform);
            }

            HostedViews.Remove(view);
        }
    }

    // Moves the real recipe views of crafts in production into the bar, returning the pixel width they use.
    private static float HostActiveViews(
        Rect bar,
        EAreaType area,
        AsyncViewList<ProductionScheme, ProduceView> viewList,
        Component windowComponent)
    {
        RestoreHostedViews(view => view.Producer == null || view.Scheme == null || !IsSchemeProducing(area, view.Scheme._id));
        var active = new List<ProduceView>();
        if (viewList != null)
        {
            foreach (ProduceView view in viewList.Views.Values)
            {
                if (view != null && view.Producer != null && view.Scheme != null &&
                    view.Producer.AreaType == area && IsSchemeProducing(area, view.Scheme._id))
                {
                    active.Add(view);
                }
            }
        }

        if (active.Count == 0)
        {
            return 0f;
        }

        RectTransform parent = windowComponent.GetComponentInParent<AreaScreenSubstrate>()?._contentLayout?.transform as RectTransform;
        if (parent == null)
        {
            return 0f;
        }

        if (_statusHost == null || _statusHost.parent != parent)
        {
            var go = new GameObject("ActiveCraftsHost", typeof(RectTransform), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _statusHost = go.GetComponent<RectTransform>();
            _statusHost.SetParent(parent, false);
            _statusHost.anchorMin = _statusHost.anchorMax = new Vector2(0f, 1f);
            _statusHost.pivot = new Vector2(0f, 1f);
        }

        _statusHost.SetAsLastSibling();
        float lossy = Mathf.Max(0.01f, parent.lossyScale.y);
        foreach (ProduceView view in active)
        {
            if (!HostedViews.ContainsKey(view))
            {
                var rect = (RectTransform)view.transform;
                HostedViews[view] = new HostedView
                {
                    Parent = view.transform.parent,
                    SiblingIndex = view.transform.GetSiblingIndex(),
                    Size = new Vector2(rect.rect.width, rect.rect.height)
                };
                view.transform.SetParent(_statusHost, false);
            }
        }

        // Positions use the views' measured screen rects, so the result doesn't depend on their original anchors.
        const float topGap = 20f;
        const float bottomGap = 14f;
        float targetHeight = bar.height - topGap - bottomGap;
        float gap = 8f;
        float startX = bar.x + 58f;
        float maxWidth = bar.width * 0.6f;
        float allowedWidth = (maxWidth - gap * (active.Count - 1)) / active.Count;

        Camera camera = null;
        Canvas canvas = parent.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            camera = canvas.worldCamera;
        }

        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            parent, new Vector2(startX, Screen.height - bar.y), camera, out Vector3 world);
        _statusHost.position = world;
        _statusHost.sizeDelta = Vector2.zero;
        _statusHost.localScale = Vector3.one;

        float cursor = startX;
        foreach (ProduceView view in active)
        {
            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = HostedViews[view].Size;
            if (!TryGetScreenRect(view, out Rect measured))
            {
                continue;
            }

            float current = Mathf.Max(0.05f, rect.localScale.x);
            float scaled = Mathf.Min(
                1.6f,
                current * targetHeight / measured.height,
                current * allowedWidth / measured.width);
            rect.localScale = Vector3.one * scaled;
            if (!TryGetScreenRect(view, out measured))
            {
                continue;
            }

            float dx = cursor - measured.x;
            float dy = measured.y - (bar.y + topGap + (targetHeight - measured.height) / 2f);
            rect.anchoredPosition += new Vector2(dx, dy) / lossy;
            cursor += measured.width + gap;
        }

        return cursor - gap - (bar.x + 10f);
    }
    private static bool IsSchemeProducing(EAreaType area, string schemeId)
    {
        return TryFindProducer(area, schemeId, out ItemsProducerBase producer, out _) &&
            producer.ProducingItems != null &&
            producer.ProducingItems.ContainsKey(schemeId);
    }
    private static void DrawCraftStatusBar(Rect bar, EAreaType area, bool inputEnabled, AsyncViewList<ProductionScheme, ProduceView> viewList, Component windowComponent)
    {
        bool repaint = Event.current.type == EventType.Repaint;

        float x0 = bar.x + 10f;
        float top = bar.y + 4f;
        float inner = bar.height - 8f;
        float leftWidth = HostActiveViews(bar, area, viewList, windowComponent);
        if (leftWidth <= 0f)
        {
            DrawOutlinedLabel(new Rect(x0, top, 130f, inner), "Active: none", _tooltipBodyStyle);
            leftWidth = 130f;
        }
        float rightX = x0 + leftWidth + 14f;
        var right = new Rect(rightX, top, bar.xMax - 10f - rightX, inner);
        if (repaint)
        {
            // The left part is left clear so the real recipe views underneath show through.
            FillRect(new Rect(rightX - 6f, bar.y, bar.xMax - rightX + 6f, bar.height), new Color(0.035f, 0.045f, 0.055f, 1f));
            DrawBorder(bar, new Color(0.68f, 0.78f, 0.86f, 0.82f), 1.5f);
        }

        if (right.width < 60f)
        {
            return;
        }
        List<QueuedCraft> entries = CraftQueue.Where(entry => entry.Area == area).ToList();
        if (entries.Count == 0)
        {
            DrawOutlinedLabel(right, "Queue: empty", _tooltipBodyStyle);
            return;
        }

        RefreshQueueNeeds(false);
        bool canBuy = QueueNeeds.Any(need => need.Buyable);
        const float iconSize = 52f;
        const float iconGap = 8f;
        const float buttonHeight = 28f;
        const float rowTop = 20f;
        float rowY = bar.y + rowTop;
        int capacity = Mathf.Max(1, (int)((right.width + iconGap) / (iconSize + iconGap)));
        int shown = Mathf.Min(entries.Count, entries.Count > capacity ? capacity - 1 : capacity);
        int slots = shown + (shown < entries.Count ? 1 : 0);
        float rowWidth = slots * iconSize + (slots - 1) * iconGap;
        float x = right.x + (right.width - rowWidth) / 2f;
        QueuedCraft toCancel = null;
        for (int i = 0; i < shown; i++)
        {
            QueuedCraft entry = entries[i];
            bool affordable = !UnaffordableQueueEntries.Contains(entry);
            Color accent = affordable ? new Color(0.25f, 0.6f, 0.95f, 1f) : new Color(0.9f, 0.2f, 0.15f, 1f);
            var chip = new Rect(x, rowY, iconSize, iconSize);
            if (repaint)
            {
                FillRect(chip, new Color(accent.r, accent.g, accent.b, 0.22f));
                DrawBorder(chip, accent, 2f);
            }

            DrawItemIcon(new Rect(chip.x + 3f, chip.y + 3f, chip.width - 6f, chip.height - 6f), GetSchemeOutputItem(area, entry.SchemeId));
            var closeRect = new Rect(chip.xMax - 16f, chip.y, 16f, 16f);
            bool hovered = inputEnabled && closeRect.Contains(Event.current.mousePosition);
            if (repaint)
            {
                FillRect(closeRect, hovered ? new Color(0.9f, 0.2f, 0.15f, 1f) : new Color(0.5f, 0.1f, 0.08f, 0.9f));
            }

            DrawOutlinedLabel(closeRect, "x", _tooltipBodyStyle);
            if (hovered && IsMouseDown(0))
            {
                toCancel = entry;
                Event.current.Use();
            }

            x += iconSize + iconGap;
        }

        if (shown < entries.Count)
        {
            DrawOutlinedLabel(new Rect(x, rowY, iconSize, iconSize), $"+{entries.Count - shown}", _tooltipBodyStyle);
        }

        if (canBuy)
        {
            float buttonWidth = Mathf.Min(right.width, 270f);
            var buyRect = new Rect(right.x + (right.width - buttonWidth) / 2f, rowY + iconSize + 10f, buttonWidth, buttonHeight);
            if (inputEnabled && DrawQueuePanelButton(buyRect, "BUY MISSING ON FLEA MARKET"))
            {
                OpenQueueShoppingList(area);
            }
        }

        if (toCancel != null)
        {
            CraftQueue.Remove(toCancel);
            SaveQueue();
            RefreshQueueNeeds(true);
            foreach (ProduceView view in ActiveProduceViews.ToList())
            {
                OnProduceViewButtonsUpdated(view);
            }
        }
    }

    private static Item GetSchemeOutputItem(EAreaType area, string schemeId)
    {
        if (!TryFindProducer(area, schemeId, out _, out ProductionScheme scheme))
        {
            return null;
        }

        string key = $"{area}:{schemeId}";
        if (!RecipeOutputItems.TryGetValue(key, out Item item) && Singleton<ItemFactory>.Instantiated)
        {
            item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), scheme.endProduct, null);
            RecipeOutputItems[key] = item;
        }

        return item;
    }
}