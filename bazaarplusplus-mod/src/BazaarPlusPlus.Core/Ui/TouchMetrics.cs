#nullable enable

namespace BazaarPlusPlus.Core.Ui;

public readonly struct TouchMetrics
{
    /// <summary>The density Android's dp is defined against.</summary>
    public const float BaselineDpi = 160f;

    /// <summary>What a screen that reports no density is taken to have: a typical phone.</summary>
    public const float FallbackDpi = 400f;

    /// <summary>The smallest touch target, Android's accessibility minimum.</summary>
    public const float MinTargetDp = 48f;

    public const float MinBodySp = 12f;

    private TouchMetrics(float canvasWidth, float canvasHeight, float unitsPerDp, TouchRect safe)
    {
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        UnitsPerDp = unitsPerDp;
        SafeArea = safe;
    }

    public float CanvasWidth { get; }

    public float CanvasHeight { get; }

    public float UnitsPerDp { get; }

    public TouchRect SafeArea { get; }

    public float MinTarget => Dp(MinTargetDp);

    public float BodyText => Sp(MinBodySp);

    // Unity reports the safe area's y origin from the bottom of the screen.
    public static TouchMetrics From(
        float screenWidth,
        float screenHeight,
        float dpi,
        float referenceHeight,
        float safeX,
        float safeY,
        float safeWidth,
        float safeHeight
    )
    {
        if (screenWidth <= 0f || screenHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(screenHeight), "The screen has no size.");
        if (referenceHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(referenceHeight));
        var density = EffectiveDpi(dpi);
        var unitsPerPixel = referenceHeight / screenHeight;
        var canvasWidth = screenWidth * unitsPerPixel;
        var safe = new TouchRect(0f, 0f, canvasWidth, referenceHeight);
        if (safeWidth > 0f && safeHeight > 0f)
        {
            var left = Math.Clamp(safeX, 0f, screenWidth);
            var bottom = Math.Clamp(safeY, 0f, screenHeight);
            var right = Math.Clamp(safeX + safeWidth, left, screenWidth);
            var top = Math.Clamp(safeY + safeHeight, bottom, screenHeight);
            safe = new TouchRect(
                left * unitsPerPixel,
                (screenHeight - top) * unitsPerPixel,
                (right - left) * unitsPerPixel,
                (top - bottom) * unitsPerPixel
            );
        }
        return new TouchMetrics(
            canvasWidth,
            referenceHeight,
            density / BaselineDpi * unitsPerPixel,
            safe
        );
    }

    public static float EffectiveDpi(float dpi) =>
        dpi > 0f && !float.IsNaN(dpi) && !float.IsInfinity(dpi) ? dpi : FallbackDpi;

    public static float MissingTargetPixels(float heightPixels, float dpi) =>
        Math.Max(0f, MinTargetDp * EffectiveDpi(dpi) / BaselineDpi - Math.Max(0f, heightPixels));

    // The native label caps the size of appended preview text so it cannot render larger than surrounding text.
    public static int ReadablePercent(int percent, float fontPixels, float dpi)
    {
        var clamped = Math.Clamp(percent, 1, 100);
        if (fontPixels <= 0f || float.IsNaN(fontPixels) || float.IsInfinity(fontPixels))
            return clamped;
        var minimumPixels = MinBodySp * EffectiveDpi(dpi) / BaselineDpi;
        var needed = (int)Math.Ceiling(minimumPixels / fontPixels * 100f - 0.001f);
        return Math.Clamp(Math.Max(clamped, needed), 1, 100);
    }

    public float Dp(float dp) => dp * UnitsPerDp;

    // The host doesn't read the system font scale, so sp uses the dp conversion.
    public float Sp(float sp) => Math.Max(sp, MinBodySp) * UnitsPerDp;

    public float Target(float dp) => Dp(Math.Max(dp, MinTargetDp));

    public static int ColumnCount(float available, float minCell, float gap, int max)
    {
        if (max < 1)
            throw new ArgumentOutOfRangeException(nameof(max));
        if (minCell <= 0f)
            return max;
        var columns = (int)Math.Floor((available + gap) / (minCell + gap));
        return Math.Clamp(columns, 1, max);
    }
}

/// <summary>A rectangle in canvas units, top-down: <see cref="Top"/> grows downwards.</summary>
public readonly record struct TouchRect(float Left, float Top, float Width, float Height)
{
    public float Right => Left + Width;

    public float Bottom => Top + Height;

    public float CenterX => Left + Width / 2f;

    public float CenterY => Top + Height / 2f;

    public TouchRect Inset(float inset) => Inset(inset, inset, inset, inset);

    public TouchRect Inset(float left, float top, float right, float bottom) =>
        new(
            Left + left,
            Top + top,
            Math.Max(0f, Width - left - right),
            Math.Max(0f, Height - top - bottom)
        );

    public bool Contains(TouchRect other, float tolerance = 0.01f) =>
        other.Left >= Left - tolerance
        && other.Top >= Top - tolerance
        && other.Right <= Right + tolerance
        && other.Bottom <= Bottom + tolerance;

    public bool Overlaps(TouchRect other, float tolerance = 0.01f) =>
        Left < other.Right - tolerance
        && other.Left < Right - tolerance
        && Top < other.Bottom - tolerance
        && other.Top < Bottom - tolerance;
}
