#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Patches.Settings;

internal static class SettingsMenuLayoutUtility
{
    private const float FallbackSpacing = 8f;

    internal static void Rebuild(RectTransform rectTransform)
    {
        var current = rectTransform;
        while (current != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(current);
            current = current.parent as RectTransform;
        }
    }

    internal static void ArrangeRow(Transform anchorRow, Transform cloneRow)
    {
        if (anchorRow == null || cloneRow == null)
            return;

        cloneRow.SetSiblingIndex(anchorRow.GetSiblingIndex() + 1);

        var parentRect = anchorRow.parent as RectTransform;
        if (parentRect == null)
            return;

        if (HasAutomaticLayout(parentRect))
        {
            BppLog.Debug("SettingsMenu", $"Using automatic layout for {cloneRow.name}");
            Rebuild(parentRect);
            return;
        }

        var anchorRect = anchorRow as RectTransform;
        var cloneRect = cloneRow as RectTransform;
        if (anchorRect == null || cloneRect == null)
        {
            Rebuild(parentRect);
            return;
        }

        var additionalIndex = GetAdditionalRowIndex(parentRect, anchorRow, cloneRow);

        var step = GetVerticalStep(anchorRect, cloneRect);
        cloneRect.anchorMin = anchorRect.anchorMin;
        cloneRect.anchorMax = anchorRect.anchorMax;
        cloneRect.pivot = anchorRect.pivot;
        cloneRect.sizeDelta = anchorRect.sizeDelta;
        cloneRect.anchoredPosition =
            anchorRect.anchoredPosition + new Vector2(0f, -step * (additionalIndex + 1));
        cloneRect.localScale = anchorRect.localScale;
        cloneRect.localRotation = anchorRect.localRotation;

        BppLog.Info(
            "SettingsMenu",
            $"Positioned {cloneRow.name} below {anchorRow.name}: index={additionalIndex + 1}, step={step:F1}, position={cloneRect.anchoredPosition}"
        );
        ExpandParentIfNeeded(parentRect, anchorRect, step, additionalIndex + 1);
    }

    private static bool HasAutomaticLayout(RectTransform rectTransform)
    {
        return rectTransform.GetComponent<VerticalLayoutGroup>() != null
            || rectTransform.GetComponent<HorizontalOrVerticalLayoutGroup>() != null
            || rectTransform.GetComponent<GridLayoutGroup>() != null;
    }

    private static int GetAdditionalRowIndex(
        RectTransform parentRect,
        Transform anchorRow,
        Transform cloneRow
    )
    {
        var index = 0;
        for (var childIndex = 0; childIndex < parentRect.childCount; childIndex++)
        {
            var child = parentRect.GetChild(childIndex);
            if (child == null || child == anchorRow || !child.name.StartsWith("BPP_"))
                continue;

            if (child == cloneRow)
                return index;

            index++;
        }

        return 0;
    }

    private static float GetVerticalStep(RectTransform anchorRect, RectTransform cloneRect)
    {
        var preferredHeight = LayoutUtility.GetPreferredHeight(anchorRect);
        if (preferredHeight <= 0f)
            preferredHeight = anchorRect.rect.height;
        if (preferredHeight <= 0f)
        {
            preferredHeight = LayoutUtility.GetPreferredHeight(cloneRect);
            if (preferredHeight <= 0f)
                preferredHeight = cloneRect.rect.height;
        }

        if (preferredHeight <= 0f)
            preferredHeight = FallbackSpacing;

        var spacing = GetSpacing(anchorRect);
        return preferredHeight + spacing;
    }

    private static float GetSpacing(RectTransform anchorRect)
    {
        var layoutElement = anchorRect.GetComponent<LayoutElement>();
        if (layoutElement != null && layoutElement.minHeight > 0f)
            return Math.Max(layoutElement.minHeight - anchorRect.rect.height, 0f);

        return FallbackSpacing;
    }

    private static void ExpandParentIfNeeded(
        RectTransform parentRect,
        RectTransform anchorRect,
        float step,
        int additionalRows
    )
    {
        var bottomY =
            anchorRect.anchoredPosition.y - step * additionalRows - anchorRect.rect.height;
        var requiredHeight = Math.Abs(Math.Min(0f, bottomY)) + anchorRect.rect.height;
        if (requiredHeight <= parentRect.rect.height)
            return;

        parentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
        Rebuild(parentRect);
    }
}
