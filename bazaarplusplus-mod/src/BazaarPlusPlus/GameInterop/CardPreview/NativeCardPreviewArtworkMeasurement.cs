#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewArtworkMeasurement
{
    public static bool PrepareLayoutSize(RectTransform preview, float fallbackHeight)
    {
        var rect = preview.rect;
        if (rect.width > .01f && rect.height > .01f)
            return true;
        var fitter = preview.GetComponent<UnityEngine.UI.AspectRatioFitter>();
        if (fitter == null || !(fitter.aspectRatio > .01f) || float.IsInfinity(fitter.aspectRatio))
            return false;
        preview.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            fallbackHeight * fitter.aspectRatio
        );
        preview.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fallbackHeight);
        return true;
    }

    public static string DescribeLayout(RectTransform preview)
    {
        var fitter = preview.GetComponent<UnityEngine.UI.AspectRatioFitter>();
        var body = NativeCardPreviewReflection.TryGetArtworkRect(
            preview,
            out var artwork,
            requireVisible: false
        )
            ? $"artwork={artwork.rect},active={artwork.gameObject.activeSelf}/{artwork.gameObject.activeInHierarchy}"
            : "artwork=unavailable";
        return $"root={preview.rect},aspect={fitter?.aspectMode}:{fitter?.aspectRatio},{body}";
    }

    // SetUp hides the artwork until its owner has fitted it. Read its exact native
    // reference, including while hidden, rather than pooled frame descendants.
    public static bool TryMeasure(RectTransform preview, out Rect bounds)
    {
        bounds = default;
        if (
            !NativeCardPreviewReflection.TryGetArtworkRect(
                preview,
                out var artwork,
                requireVisible: false
            )
        )
            return false;
        var corners = new Vector3[4];
        artwork.GetWorldCorners(corners);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var corner in corners)
        {
            var local = preview.InverseTransformPoint(corner);
            minX = Math.Min(minX, local.x);
            minY = Math.Min(minY, local.y);
            maxX = Math.Max(maxX, local.x);
            maxY = Math.Max(maxY, local.y);
        }
        if (
            !(maxX - minX > .01f && maxY - minY > .01f)
            || float.IsInfinity(maxX - minX)
            || float.IsInfinity(maxY - minY)
        )
            return false;
        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }
}
