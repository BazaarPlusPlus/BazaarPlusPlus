#nullable enable

namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// Physical sizes on a touch screen, in the units of a canvas scaled by screen height (the
/// Android host's overlay canvas: a reference height, width following the aspect). A touch panel
/// sizes its targets and type from these instead of from canvas units, so a 48dp target is a
/// fingertip at any resolution or density, and it lays out inside <see cref="SafeArea"/>.
/// Unity-free: a host passes <c>Screen.width</c>, <c>Screen.height</c>, <c>Screen.dpi</c>, and
/// <c>Screen.safeArea</c> in, and Core tests pin the arithmetic.
/// </summary>
public readonly struct TouchMetrics
{
    /// <summary>The density Android's dp is defined against.</summary>
    public const float BaselineDpi = 160f;

    /// <summary>What a screen that reports no density is taken to have: a typical phone.</summary>
    public const float FallbackDpi = 400f;

    /// <summary>The smallest touch target, Android's accessibility minimum.</summary>
    public const float MinTargetDp = 48f;

    /// <summary>The smallest body text.</summary>
    public const float MinBodySp = 12f;

    private TouchMetrics(float canvasWidth, float canvasHeight, float unitsPerDp, TouchRect safe)
    {
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        UnitsPerDp = unitsPerDp;
        SafeArea = safe;
    }

    /// <summary>The canvas width in its own units.</summary>
    public float CanvasWidth { get; }

    /// <summary>The canvas height in its own units: the reference height.</summary>
    public float CanvasHeight { get; }

    /// <summary>Canvas units per density-independent pixel.</summary>
    public float UnitsPerDp { get; }

    /// <summary>The part of the canvas no cutout, rounded corner, or system bar covers, top-down.</summary>
    public TouchRect SafeArea { get; }

    /// <summary>The smallest touch target in canvas units.</summary>
    public float MinTarget => Dp(MinTargetDp);

    /// <summary>The smallest body text size in canvas units.</summary>
    public float BodyText => Sp(MinBodySp);

    /// <param name="screenWidth">The screen width in pixels.</param>
    /// <param name="screenHeight">The screen height in pixels.</param>
    /// <param name="dpi">The reported density; zero or less (unknown) reads as <see cref="FallbackDpi"/>.</param>
    /// <param name="referenceHeight">The canvas height the screen height scales to.</param>
    /// <param name="safeX">The safe area's left edge in pixels, from the left.</param>
    /// <param name="safeY">The safe area's bottom edge in pixels, from the bottom (Unity's convention).</param>
    /// <param name="safeWidth">The safe area's width in pixels; zero or less means the whole screen.</param>
    /// <param name="safeHeight">The safe area's height in pixels; zero or less means the whole screen.</param>
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

    /// <summary>The density a screen is laid out for: the reported one, or <see cref="FallbackDpi"/>.</summary>
    public static float EffectiveDpi(float dpi) =>
        dpi > 0f && !float.IsNaN(dpi) && !float.IsInfinity(dpi) ? dpi : FallbackDpi;

    /// <summary>
    /// How many screen pixels a control <paramref name="heightPixels"/> tall lacks to be a
    /// <see cref="MinTargetDp"/> target at <paramref name="dpi"/>; zero when it is one already. For a
    /// control on a canvas other than the touch canvas, such as the game's own dialogs.
    /// </summary>
    public static float MissingTargetPixels(float heightPixels, float dpi) =>
        Math.Max(0f, MinTargetDp * EffectiveDpi(dpi) / BaselineDpi - Math.Max(0f, heightPixels));

    /// <summary><paramref name="dp"/> density-independent pixels in canvas units.</summary>
    public float Dp(float dp) => dp * UnitsPerDp;

    /// <summary>
    /// A font size of <paramref name="sp"/> scale-independent pixels in canvas units, never under
    /// <see cref="MinBodySp"/>. The system font scale is not read, so sp equals dp.
    /// </summary>
    public float Sp(float sp) => Math.Max(sp, MinBodySp) * UnitsPerDp;

    /// <summary>A target size, at least <see cref="MinTargetDp"/>.</summary>
    public float Target(float dp) => Dp(Math.Max(dp, MinTargetDp));

    /// <summary>
    /// How many cells of at least <paramref name="minCell"/> fit across <paramref name="available"/>
    /// with <paramref name="gap"/> between them: at least one, at most <paramref name="max"/>.
    /// </summary>
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

    /// <summary>The rectangle shrunk by <paramref name="inset"/> on every side, never below zero size.</summary>
    public TouchRect Inset(float inset) => Inset(inset, inset, inset, inset);

    public TouchRect Inset(float left, float top, float right, float bottom) =>
        new(
            Left + left,
            Top + top,
            Math.Max(0f, Width - left - right),
            Math.Max(0f, Height - top - bottom)
        );

    /// <summary>Whether <paramref name="other"/> lies wholly inside this one, to a rounding tolerance.</summary>
    public bool Contains(TouchRect other, float tolerance = 0.01f) =>
        other.Left >= Left - tolerance
        && other.Top >= Top - tolerance
        && other.Right <= Right + tolerance
        && other.Bottom <= Bottom + tolerance;

    /// <summary>Whether the two overlap by more than a rounding tolerance.</summary>
    public bool Overlaps(TouchRect other, float tolerance = 0.01f) =>
        Left < other.Right - tolerance
        && other.Left < Right - tolerance
        && Top < other.Bottom - tolerance
        && other.Top < Bottom - tolerance;
}
