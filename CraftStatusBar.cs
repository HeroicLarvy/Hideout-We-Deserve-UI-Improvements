using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Hideout;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;

namespace HideoutWeDeserveUIImprovements;

// A fixed bar for active crafts under the folder bar.
public sealed partial class HideoutWeDeserveUIImprovementsPlugin
{
    private const float StatusBarHeight = 150f;
    private const float StatusBarTopGap = 40f;
    private const float StatusBarBottomGap = 14f;
    private const float StatusBarSideGap = 10f;
    private const float ActiveViewGap = 8f;

    private sealed class HostedView
    {
        public Transform Parent;
        public int SiblingIndex;
        public Vector2 Size;
        public Vector2 ScreenSize;
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

    private static List<ProduceView> GetActiveCraftViews(
        EAreaType area,
        AsyncViewList<ProductionScheme, ProduceView> viewList)
    {
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

        return active;
    }

    private static float GetStatusBarHeight(EAreaType area, AsyncViewList<ProductionScheme, ProduceView> viewList, float maxHeight)
    {
        float height = StatusBarHeight;
        foreach (ProduceView view in GetActiveCraftViews(area, viewList))
        {
            float viewHeight;
            if (HostedViews.TryGetValue(view, out HostedView hosted))
            {
                viewHeight = hosted.ScreenSize.y;
            }
            else if (TryGetScreenRect(view, out Rect rect))
            {
                viewHeight = rect.height;
            }
            else
            {
                continue;
            }

            height = Mathf.Max(height, viewHeight + StatusBarTopGap + StatusBarBottomGap);
        }

        return Mathf.Min(height, Mathf.Max(StatusBarHeight, maxHeight));
    }

    // Moves the real recipe views of crafts in production into the bar.
    private static Rect HostActiveViews(
        Rect bar,
        EAreaType area,
        AsyncViewList<ProductionScheme, ProduceView> viewList,
        Component windowComponent)
    {
        RestoreHostedViews(view => view.Producer == null || view.Scheme == null || !IsSchemeProducing(area, view.Scheme._id));
        List<ProduceView> active = GetActiveCraftViews(area, viewList);
        if (active.Count == 0)
        {
            return default;
        }

        RectTransform parent = windowComponent.GetComponentInParent<AreaScreenSubstrate>()?._contentLayout?.transform as RectTransform;
        if (parent == null)
        {
            return default;
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
                if (!TryGetScreenRect(view, out Rect originalRect) || originalRect.width <= 0f || originalRect.height <= 0f)
                {
                    continue;
                }

                HostedViews[view] = new HostedView
                {
                    Parent = view.transform.parent,
                    SiblingIndex = view.transform.GetSiblingIndex(),
                    Size = new Vector2(rect.rect.width, rect.rect.height),
                    ScreenSize = new Vector2(originalRect.width, originalRect.height)
                };
                view.transform.SetParent(_statusHost, false);
            }
        }

        // Positions use the views' measured screen rects, so the result doesn't depend on their original anchors.
        active.RemoveAll(view => !HostedViews.ContainsKey(view));
        if (active.Count == 0)
        {
            return default;
        }

        float targetHeight = Mathf.Max(1f, bar.height - StatusBarTopGap - StatusBarBottomGap);
        float allowedWidth = Mathf.Max(1f, (bar.width - 2f * StatusBarSideGap - ActiveViewGap * (active.Count - 1)) / active.Count);

        Camera camera = null;
        Canvas canvas = parent.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            camera = canvas.worldCamera;
        }

        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            parent, new Vector2(bar.x, Screen.height - bar.y), camera, out Vector3 world);
        _statusHost.position = world;
        _statusHost.sizeDelta = Vector2.zero;
        _statusHost.localScale = Vector3.one;

        var measuredViews = new List<(RectTransform Transform, Rect ScreenRect)>();
        float totalWidth = 0f;
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
            if (measured.width <= 0f || measured.height <= 0f)
            {
                continue;
            }

            Vector2 originalSize = HostedViews[view].ScreenSize;
            float scaled = current * Mathf.Min(
                originalSize.y / measured.height,
                originalSize.x / measured.width,
                targetHeight / measured.height,
                allowedWidth / measured.width);
            rect.localScale = Vector3.one * scaled;
            if (!TryGetScreenRect(view, out measured))
            {
                continue;
            }

            measuredViews.Add((rect, measured));
            totalWidth += measured.width;
        }

        if (measuredViews.Count == 0)
        {
            return default;
        }

        totalWidth += ActiveViewGap * (measuredViews.Count - 1);
        float cursor = bar.center.x - totalWidth / 2f;
        float startX = cursor;
        foreach ((RectTransform rect, Rect measured) in measuredViews)
        {
            float dx = cursor - measured.x;
            float dy = measured.y - (bar.y + StatusBarTopGap + (targetHeight - measured.height) / 2f);
            rect.anchoredPosition += new Vector2(dx, dy) / lossy;
            cursor += measured.width + ActiveViewGap;
        }

        return new Rect(startX, bar.y, totalWidth, bar.height);
    }
    private static bool IsSchemeProducing(EAreaType area, string schemeId)
    {
        return TryFindProducer(area, schemeId, out ItemsProducerBase producer, out _) &&
            producer.ProducingItems != null &&
            producer.ProducingItems.ContainsKey(schemeId);
    }

    private static void DrawCraftStatusBar(Rect bar, EAreaType area, AsyncViewList<ProductionScheme, ProduceView> viewList, Component windowComponent)
    {
        Rect activeBounds = HostActiveViews(bar, area, viewList, windowComponent);
        if (Event.current.type == EventType.Repaint)
        {
            if (activeBounds.width > 0f)
            {
                FillRect(new Rect(bar.x, bar.y, activeBounds.xMin - bar.x, bar.height), new Color(0.035f, 0.045f, 0.055f, 1f));
                FillRect(new Rect(activeBounds.xMax, bar.y, bar.xMax - activeBounds.xMax, bar.height), new Color(0.035f, 0.045f, 0.055f, 1f));
            }
            else
            {
                FillRect(bar, new Color(0.035f, 0.045f, 0.055f, 1f));
            }

            DrawBorder(bar, new Color(0.68f, 0.78f, 0.86f, 0.82f), 1.5f);
        }

        if (activeBounds.width <= 0f)
        {
            DrawOutlinedLabel(new Rect(bar.x + 10f, bar.y + 4f, 130f, bar.height - 8f), "Active: none", _tooltipBodyStyle);
        }
    }
}