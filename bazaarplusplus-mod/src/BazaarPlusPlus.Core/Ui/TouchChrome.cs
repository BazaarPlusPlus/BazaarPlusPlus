#nullable enable

namespace BazaarPlusPlus.Core.Ui;

// Touch layouts share this frame in top-down canvas units. See docs/architecture/mobile-ui.md.
public sealed class TouchChrome
{
    public const float MarginDp = 4f;
    public const float PaddingDp = 8f;
    public const float GapDp = 8f;
    public const float CloseWidthDp = 64f;

    public const float LabelInsetDp = 6f;

    public const float TitleSp = 18f;

    public const float NameSp = 20f;

    public const float ButtonSp = 14f;

    public const float BodySp = TouchMetrics.MinBodySp;

    public const float ClockTimeSp = 22f;

    public const float ClockSpeedSp = 16f;

    private TouchChrome(TouchMetrics metrics)
    {
        Metrics = metrics;
        Margin = metrics.Dp(MarginDp);
        Padding = metrics.Dp(PaddingDp);
        Gap = metrics.Dp(GapDp);
        Target = metrics.MinTarget;
        CloseWidth = metrics.Target(CloseWidthDp);
        LabelInset = metrics.Dp(LabelInsetDp);
        TitleFont = metrics.Sp(TitleSp);
        NameFont = metrics.Sp(NameSp);
        ButtonFont = metrics.Sp(ButtonSp);
        BodyFont = metrics.Sp(BodySp);
        ClockTimeFont = metrics.Sp(ClockTimeSp);
        ClockSpeedFont = metrics.Sp(ClockSpeedSp);
        Sheet = metrics.SafeArea.Inset(Margin);
        Body = Sheet.Inset(Padding);
        Close = CloseAt(Body);
        Title = TitleBeside(Body, Close);
    }

    public static TouchChrome For(TouchMetrics metrics) => new(metrics);

    public TouchMetrics Metrics { get; }

    public float Margin { get; }

    public float Padding { get; }

    public float Gap { get; }

    public float Target { get; }

    public float CloseWidth { get; }

    public float LabelInset { get; }

    public float TitleFont { get; }

    public float NameFont { get; }

    public float ButtonFont { get; }

    public float BodyFont { get; }

    public float ClockTimeFont { get; }

    public float ClockSpeedFont { get; }

    public TouchRect Sheet { get; }

    public TouchRect Body { get; }

    public TouchRect Close { get; }

    public TouchRect Title { get; }

    public TouchRect CloseAt(TouchRect area) =>
        new(area.Right - CloseWidth, area.Top, CloseWidth, Target);

    public TouchRect TitleBeside(TouchRect area, TouchRect close) =>
        new(area.Left, area.Top, Math.Max(0f, close.Left - Gap - area.Left), Target);

    public TouchInspectLayout Inspect() => new(this);

    public static int ChipSpan(float rowWidth, int columns, int span, float gap, float chipWidth)
    {
        columns = Math.Max(1, columns);
        span = Math.Clamp(span, 1, columns);
        if (!(rowWidth > 0f) || !(chipWidth > 0f))
            return span;
        while (span < columns && rowWidth * span / columns - gap < chipWidth)
            span++;
        return span;
    }
}

public sealed class TouchInspectLayout
{
    public const float CardFraction = 0.55f;

    // Leave room for the native card frame's glow.
    public const float CardFill = 0.9f;

    private const float NameDp = 64f;

    internal TouchInspectLayout(TouchChrome chrome)
    {
        var body = chrome.Body;
        CardArea = new TouchRect(body.Left, body.Top, body.Width * CardFraction, body.Height);
        Info = new TouchRect(
            CardArea.Right + chrome.Padding,
            body.Top,
            Math.Max(0f, body.Right - CardArea.Right - chrome.Padding),
            body.Height
        );
        Close = chrome.CloseAt(Info);
        Name = new TouchRect(
            Info.Left,
            Info.Top,
            Math.Max(0f, Close.Left - chrome.Gap - Info.Left),
            Math.Max(chrome.Target, chrome.Metrics.Dp(NameDp))
        );
        Detail = new TouchRect(
            Info.Left,
            Name.Bottom + chrome.Gap,
            Info.Width,
            Math.Max(0f, Info.Bottom - Name.Bottom - chrome.Gap)
        );
    }

    public TouchRect CardArea { get; }

    // Taps inside the info column leave the sheet open.
    public TouchRect Info { get; }

    public TouchRect Name { get; }

    public TouchRect Close { get; }

    public TouchRect Detail { get; }

    public float Scale(float naturalWidth, float naturalHeight)
    {
        if (naturalWidth <= 0f || naturalHeight <= 0f)
            return 0.01f;
        return Math.Max(
            0.01f,
            Math.Min(
                CardArea.Height * CardFill / naturalHeight,
                CardArea.Width * CardFill / naturalWidth
            )
        );
    }
}
