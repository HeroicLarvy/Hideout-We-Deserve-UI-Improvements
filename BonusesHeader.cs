using System.Collections;
using EFT.Hideout;
using UnityEngine;
using UnityEngine.UI;

namespace HideoutWeDeserveUIImprovements;

// Moves the "Current bonuses" panel out of the scrolling station content and into the window header.
public sealed partial class HideoutWeDeserveUIImprovementsPlugin
{
    private const float BonusesHeaderRightMargin = 16f;
    private const int BonusesHeaderSettleFrames = 30;

    private static void OnBonusesPanelShown(BonusesPanel __instance)
    {
        if (__instance == null || __instance.LevelType != ELevelType.Current)
        {
            return;
        }

        // The view list re-parents and re-orders the panel right after Show, so wait a frame.
        _instance.StartCoroutine(MoveBonusesToHeader(__instance));
    }

    private static IEnumerator MoveBonusesToHeader(BonusesPanel panel)
    {
        yield return null;
        if (panel == null)
        {
            yield break;
        }

        AreaScreenSubstrate substrate = panel.GetComponentInParent<AreaScreenSubstrate>();
        Transform headerTransform = substrate?._closeButton != null
            ? substrate._closeButton.transform.parent
            : substrate?._areaPanel?.transform;
        if (headerTransform is not RectTransform header || panel.transform is not RectTransform rect)
        {
            yield break;
        }

        rect.SetParent(header, false);
        rect.SetAsLastSibling();
        LayoutElement layout = panel.GetComponent<LayoutElement>() ?? panel.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);

        float closeOffset = 0f;
        if (substrate._closeButton != null &&
            substrate._closeButton.transform is RectTransform close &&
            close.parent == header)
        {
            closeOffset = close.rect.width + 12f;
        }

        // Content loads asynchronously, so keep fitting the panel to the header while it settles.
        for (int frame = 0; frame < BonusesHeaderSettleFrames && panel != null; frame++)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            float width = LayoutUtility.GetPreferredWidth(rect);
            float height = LayoutUtility.GetPreferredHeight(rect);
            if (width > 1f && height > 1f)
            {
                float scale = Mathf.Min(1f, header.rect.height / height);
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = new Vector2(-(BonusesHeaderRightMargin + closeOffset), 0f);
            }

            yield return null;
        }
    }
}
