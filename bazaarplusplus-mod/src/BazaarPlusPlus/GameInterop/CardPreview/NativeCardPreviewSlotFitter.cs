#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewSlotFitter
{
    internal static NativeCardPreviewSlotFitResult Fit(
        RectTransform preview,
        RectTransform slot,
        NativeCardPreviewHorizontalAlignment horizontalAlignment
    )
    {
        if (preview == null || slot == null)
            return NativeCardPreviewSlotFitResult.Unavailable;

        Canvas.ForceUpdateCanvases();
        return FitWithSettledCanvas(preview, slot, horizontalAlignment, new Vector3[4]);
    }

    /// <summary>
    /// Fits one preview after the caller has settled the Canvas shared by a larger layout batch.
    /// </summary>
    internal static NativeCardPreviewSlotFitResult FitWithSettledCanvas(
        RectTransform preview,
        RectTransform slot,
        NativeCardPreviewHorizontalAlignment horizontalAlignment,
        Vector3[] corners
    )
    {
        if (preview == null || slot == null || corners == null || corners.Length < 4)
            return NativeCardPreviewSlotFitResult.Unavailable;

        var frame = FindDescendant(preview, "FrameContainer") ?? preview;
        frame.GetWorldCorners(corners);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var corner in corners)
        {
            var local = preview.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxX = Mathf.Max(maxX, local.x);
            maxY = Mathf.Max(maxY, local.y);
        }

        var slotRect = slot.rect;
        if (
            !NativeCardPreviewSlotFitMath.TryResolve(
                minX,
                minY,
                maxX,
                maxY,
                slotRect.width,
                slotRect.height,
                slotRect.center.x,
                slotRect.center.y,
                horizontalAlignment,
                out var fit
            )
        )
            return NativeCardPreviewSlotFitResult.Unavailable;

        preview.localRotation = Quaternion.identity;
        preview.localScale = new Vector3(fit.Scale, fit.Scale, 1f);
        preview.localPosition = new Vector3(fit.PositionX, fit.PositionY, preview.localPosition.z);
        if (horizontalAlignment == NativeCardPreviewHorizontalAlignment.Left)
            slot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fit.VisibleWidth);
        return NativeCardPreviewSlotFitResult.Applied;
    }

    internal static bool TryAlignVisibleArtworkLeft(
        RectTransform preview,
        RectTransform slot,
        out float visibleWidth
    )
    {
        visibleWidth = 0f;
        if (preview == null || slot == null)
            return false;

        Canvas.ForceUpdateCanvases();
        return TryAlignVisibleArtworkLeftWithSettledCanvas(
            preview,
            slot,
            new Vector3[4],
            out visibleWidth
        );
    }

    /// <summary>
    /// Aligns visible artwork after the caller has settled the Canvas shared by a larger layout
    /// batch.
    /// </summary>
    internal static bool TryAlignVisibleArtworkLeftWithSettledCanvas(
        RectTransform preview,
        RectTransform slot,
        Vector3[] corners,
        out float visibleWidth
    )
    {
        visibleWidth = 0f;
        if (preview == null || slot == null || corners == null || corners.Length < 4)
            return false;

        // CardPreviewItem.Resize offsets wider cards inside their root, while the native frame
        // prefab itself can carry transparent horizontal inset. Align the actual artwork quad,
        // which is the stable visible seam shared by Small/Medium/Large previews, instead of the
        // FrameContainer's layout bounds.
        var frame = FindNativeArtwork(preview);
        if (frame == null)
            return false;
        frame.GetWorldCorners(corners);
        var visibleMinX = float.PositiveInfinity;
        var visibleMaxX = float.NegativeInfinity;
        foreach (var corner in corners)
        {
            var localX = slot.InverseTransformPoint(corner).x;
            visibleMinX = Mathf.Min(visibleMinX, localX);
            visibleMaxX = Mathf.Max(visibleMaxX, localX);
        }

        if (
            !NativeCardPreviewSlotFitMath.TryResolveVisibleHorizontalFit(
                visibleMinX,
                visibleMaxX,
                slot.rect.xMin,
                out var fit
            )
        )
            return false;

        var correctedWorldPosition =
            preview.position + slot.TransformVector(fit.PositionCorrection, 0f, 0f);
        preview.position = correctedWorldPosition;
        visibleWidth = fit.VisibleWidth;
        return true;
    }

    private static RectTransform? FindNativeArtwork(RectTransform preview) =>
        NativeCardPreviewReflection.TryGetArtworkRect(preview, out var artwork) ? artwork : null;

    private static RectTransform? FindDescendant(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child is RectTransform rect && rect.name == name)
                return rect;
        }
        return null;
    }
}
