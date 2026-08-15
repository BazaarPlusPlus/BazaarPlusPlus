#nullable enable
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Unity adapter that measures a native card preview and fits it into a Collection grid cell.
// Measurement chain (FrameContainer → RawImage → Aspect-ratio mutation fallback → root.rect →
// sizeDelta → 1×1) runs only on cache miss / force-remeasure. Scroll reposition reads the
// per-cell NativeCardCellBoundsCache and does pure position math (zero measure).
//
// Aspect fallback decision (issue #166): SetSizeWithCurrentAnchors runs once when that branch
// measures, then the resulting bounds are cached. Scroll frames do not re-SetSize. If in-game
// smoke finds Aspect-fallback cards drifting without repeated SetSize, exempt that path from
// the cache (or re-apply SetSize from cached width/height on reposition) — do not silently
// change behavior without a recorded decision.
internal static class NativeCardCellFitter
{
    public const float FallbackNativeCardHeight = 484f;

    // Diagnostic: increments only when ResolveNativeVisualBounds runs. Scroll-only reposition
    // must leave this unchanged when the cell cache is warm.
    internal static int MeasureInvocationCount { get; private set; }

    internal static void ResetMeasureInvocationCount() => MeasureInvocationCount = 0;

    public static void ApplyScale(
        RectTransform rect,
        CollectionGridRect cellRect,
        float gap,
        NativeCardCellBoundsCache boundsCache,
        bool forceMeasure = false,
        float horizontalScale = 1f
    )
    {
        if (rect == null || boundsCache == null)
            return;
        PrepareGridRect(rect);
        var visualBounds = ResolveBounds(rect, boundsCache, forceMeasure, out var aspectRatio);
        var scale = CollectionCardFitMath.ComputeScale(
            visualBounds,
            aspectRatio,
            cellRect,
            gap,
            CollectionGridConstants.CellContentInset,
            ItemBoardSocketLayout.FrameHeightOverSocket
        );
        rect.localScale = new Vector3(scale * Mathf.Max(0.01f, horizontalScale), scale, 1f);
    }

    // allowMeasure=false is the scroll path: never walk the measurement chain. Callers must
    // keep the cache warm via ApplyScale / invalidation+remeasure on the three hooks.
    public static void Reposition(
        RectTransform rect,
        CollectionGridRect cellRect,
        float scrollY,
        NativeCardCellBoundsCache boundsCache,
        bool allowMeasure = true
    )
    {
        if (rect == null || boundsCache == null)
            return;
        PrepareGridRect(rect);
        if (!boundsCache.TryGet(out var visualBounds, out _))
        {
            if (!allowMeasure)
                return;
            visualBounds = MeasureAndStore(rect, boundsCache);
        }

        var pos = CollectionCardFitMath.ComputeAnchoredPosition(
            visualBounds,
            rect.localScale.x,
            rect.localScale.y,
            cellRect,
            scrollY
        );
        rect.anchoredPosition = new Vector2(pos.X, pos.Y);
    }

    private static CardVisualBounds ResolveBounds(
        RectTransform rect,
        NativeCardCellBoundsCache boundsCache,
        bool forceMeasure,
        out float? aspectRatio
    )
    {
        if (!forceMeasure && boundsCache.TryGet(out var cached, out aspectRatio))
            return cached;
        return MeasureAndStore(rect, boundsCache, out aspectRatio);
    }

    private static CardVisualBounds MeasureAndStore(
        RectTransform rect,
        NativeCardCellBoundsCache boundsCache
    ) => MeasureAndStore(rect, boundsCache, out _);

    private static CardVisualBounds MeasureAndStore(
        RectTransform rect,
        NativeCardCellBoundsCache boundsCache,
        out float? aspectRatio
    )
    {
        MeasureInvocationCount++;
        aspectRatio = TryReadAspectRatio(rect);
        var visualBounds = ResolveNativeVisualBounds(rect);
        boundsCache.Store(visualBounds, aspectRatio);
        return visualBounds;
    }

    private static void PrepareGridRect(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static float? TryReadAspectRatio(RectTransform rect)
    {
        var fitter = rect.GetComponent<AspectRatioFitter>();
        if (fitter == null)
            return null;
        return fitter.aspectRatio;
    }

    private static CardVisualBounds ResolveNativeVisualBounds(RectTransform root)
    {
        var frame = FindDescendant(root, "FrameContainer");
        if (frame != null && TryMeasureSubtreeBounds(root, frame, out var frameBounds))
            return frameBounds;
        if (TryMeasureRawImageBounds(root, out var imageBounds))
            return imageBounds;
        if (TryResolveAspectRatioFallbackBounds(root, out var aspectBounds))
            return aspectBounds;

        var rootRect = root.rect;
        if (IsUsableNativeSize(rootRect.width, rootRect.height))
        {
            return new CardVisualBounds(
                Mathf.Max(1f, Mathf.Abs(rootRect.width)),
                Mathf.Max(1f, Mathf.Abs(rootRect.height)),
                rootRect.center.x,
                rootRect.center.y
            );
        }

        var sizeDelta = root.sizeDelta;
        if (IsUsableNativeSize(sizeDelta.x, sizeDelta.y))
        {
            return new CardVisualBounds(
                Mathf.Max(1f, Mathf.Abs(sizeDelta.x)),
                Mathf.Max(1f, Mathf.Abs(sizeDelta.y)),
                0f,
                0f
            );
        }

        return new CardVisualBounds(1f, 1f, 0f, 0f);
    }

    private static bool TryResolveAspectRatioFallbackBounds(
        RectTransform root,
        out CardVisualBounds bounds
    )
    {
        var fitter = root.GetComponent<AspectRatioFitter>();
        if (
            fitter == null
            || fitter.aspectRatio <= 0.01f
            || float.IsNaN(fitter.aspectRatio)
            || float.IsInfinity(fitter.aspectRatio)
        )
        {
            bounds = default;
            return false;
        }

        var width = Mathf.Max(1f, FallbackNativeCardHeight * fitter.aspectRatio);
        var height = FallbackNativeCardHeight;
        // One-time SetSize on measure (cached thereafter). See class comment for the #166
        // decision if smoke finds Aspect-fallback cards needing repeated SetSize.
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        var rect = root.rect;
        bounds = new CardVisualBounds(width, height, rect.center.x, rect.center.y);
        return true;
    }

    private static bool TryMeasureRawImageBounds(RectTransform root, out CardVisualBounds bounds)
    {
        var corners = new Vector3[4];
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var found = false;

        foreach (var image in root.GetComponentsInChildren<RawImage>(true))
        {
            if (image == null || image.rectTransform == null)
                continue;
            AccumulateSingleRectBounds(
                root,
                image.rectTransform,
                corners,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY,
                ref found
            );
        }

        if (!found || !IsUsableNativeSize(maxX - minX, maxY - minY))
        {
            bounds = default;
            return false;
        }

        bounds = new CardVisualBounds(
            Mathf.Max(1f, maxX - minX),
            Mathf.Max(1f, maxY - minY),
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f
        );
        return true;
    }

    private static bool TryMeasureSubtreeBounds(
        RectTransform root,
        Transform subtree,
        out CardVisualBounds bounds
    )
    {
        var corners = new Vector3[4];
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var found = false;

        AccumulateRectBounds(
            root,
            subtree,
            corners,
            ref minX,
            ref minY,
            ref maxX,
            ref maxY,
            ref found
        );
        if (!found || !IsUsableNativeSize(maxX - minX, maxY - minY))
        {
            bounds = default;
            return false;
        }

        bounds = new CardVisualBounds(
            Mathf.Max(1f, maxX - minX),
            Mathf.Max(1f, maxY - minY),
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f
        );
        return true;
    }

    private static void AccumulateSingleRectBounds(
        RectTransform root,
        RectTransform current,
        Vector3[] corners,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY,
        ref bool found
    )
    {
        var rect = current.rect;
        if (!IsUsableNativeSize(rect.width, rect.height))
            return;

        current.GetWorldCorners(corners);
        for (var i = 0; i < corners.Length; i++)
        {
            var local = root.InverseTransformPoint(corners[i]);
            minX = Mathf.Min(minX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxX = Mathf.Max(maxX, local.x);
            maxY = Mathf.Max(maxY, local.y);
        }
        found = true;
    }

    private static void AccumulateRectBounds(
        RectTransform root,
        Transform current,
        Vector3[] corners,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY,
        ref bool found
    )
    {
        if (current is RectTransform currentRect)
            AccumulateSingleRectBounds(
                root,
                currentRect,
                corners,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY,
                ref found
            );

        foreach (Transform child in current)
        {
            AccumulateRectBounds(
                root,
                child,
                corners,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY,
                ref found
            );
        }
    }

    private static Transform? FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
                return child;

            var descendant = FindDescendant(child, name);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private static bool IsUsableNativeSize(float width, float height) =>
        width > 0.01f
        && height > 0.01f
        && !float.IsNaN(width)
        && !float.IsNaN(height)
        && !float.IsInfinity(width)
        && !float.IsInfinity(height);
}
