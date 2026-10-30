#nullable enable

namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// The frame every BPP touch surface shares, in canvas units top-down: the sheet (the safe area
/// less <see cref="MarginDp"/>), its body (less <see cref="PaddingDp"/>), the gap, the header row
/// height, the type scale, and the close button, a <see cref="CloseWidthDp"/> by
/// <see cref="TouchMetrics.MinTargetDp"/> target in the top-right corner of whatever it closes. A
/// panel's own layout (<c>CollectionTouchLayout</c>, <c>HistoryTouchLayout</c>,
/// <c>LiveBuildTouchLayout</c>, <see cref="TouchHudLayout"/>) places its content inside this
/// frame, so the panels open, read, and close alike; <see cref="Inspect"/> is the shared
/// tap-to-inspect sheet. Unity-free; the conventions are in <c>docs/architecture/android-mobile-ui.md</c>.
/// </summary>
public sealed class TouchChrome
{
    public const float MarginDp = 4f;
    public const float PaddingDp = 8f;
    public const float GapDp = 8f;
    public const float CloseWidthDp = 64f;

    /// <summary>The padding between a button's edge and its label, on either side.</summary>
    public const float LabelInsetDp = 6f;

    /// <summary>A panel or dialog title.</summary>
    public const float TitleSp = 18f;

    /// <summary>An inspected card's name.</summary>
    public const float NameSp = 20f;

    /// <summary>A button, tab, chip, or row title.</summary>
    public const float ButtonSp = 14f;

    /// <summary>Body text, notes, and status lines: <see cref="TouchMetrics.MinBodySp"/>.</summary>
    public const float BodySp = TouchMetrics.MinBodySp;

    /// <summary>The combat clock's time.</summary>
    public const float ClockTimeSp = 22f;

    /// <summary>The combat clock's speed multiplier.</summary>
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

    /// <summary>The header row's height and every button's: one <see cref="TouchMetrics.MinTarget"/>.</summary>
    public float Target { get; }

    public float CloseWidth { get; }

    /// <summary><see cref="LabelInsetDp"/> in canvas units: a label's room is its button less twice this.</summary>
    public float LabelInset { get; }

    public float TitleFont { get; }

    public float NameFont { get; }

    public float ButtonFont { get; }

    public float BodyFont { get; }

    public float ClockTimeFont { get; }

    public float ClockSpeedFont { get; }

    /// <summary>A full-screen panel: the safe area less the margin.</summary>
    public TouchRect Sheet { get; }

    /// <summary>The sheet less its padding: where a panel lays its content.</summary>
    public TouchRect Body { get; }

    /// <summary>A full-screen panel's close button, the body's top-right corner.</summary>
    public TouchRect Close { get; }

    /// <summary>A full-screen panel's title, the header row left of <see cref="Close"/>.</summary>
    public TouchRect Title { get; }

    /// <summary>The close button of a surface laid out in <paramref name="area"/>: its top-right corner.</summary>
    public TouchRect CloseAt(TouchRect area) =>
        new(area.Right - CloseWidth, area.Top, CloseWidth, Target);

    /// <summary>A header row from <paramref name="area"/>'s left edge to a gap before <paramref name="close"/>.</summary>
    public TouchRect TitleBeside(TouchRect area, TouchRect close) =>
        new(area.Left, area.Top, Math.Max(0f, close.Left - Gap - area.Left), Target);

    /// <summary>The tap-to-inspect sheet over a panel: the card on the left, its text on the right.</summary>
    public TouchInspectLayout Inspect() => new(this);

    /// <summary>
    /// How many of a <paramref name="columns"/>-column chip row's columns a chip
    /// <paramref name="chipWidth"/> units wide (label plus padding) spans: its requested
    /// <paramref name="span"/>, widened until the chip, less a <paramref name="gap"/> between it
    /// and its neighbours, holds the label whole, up to the full row. An unmeasured row or chip
    /// keeps the request.
    /// </summary>
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

/// <summary>
/// The tap-to-inspect sheet every touch panel shares: the tapped card enlarged on the left
/// <see cref="CardFraction"/> of the sheet, and an opaque info column on the right with the card's
/// name, the close button, and the detail (the card's text, and a panel's own chips above it). A
/// tap outside the info column, the close button, or the back key dismisses it.
/// </summary>
public sealed class TouchInspectLayout
{
    /// <summary>The share of the sheet's body the enlarged card takes.</summary>
    public const float CardFraction = 0.55f;

    /// <summary>The share of its area the enlarged card fills, leaving the frame's glow room.</summary>
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

    /// <summary>Where the enlarged card is centered.</summary>
    public TouchRect CardArea { get; }

    /// <summary>The opaque column beside the card; taps on it do not dismiss.</summary>
    public TouchRect Info { get; }

    public TouchRect Name { get; }

    public TouchRect Close { get; }

    /// <summary>Under the name, to the info column's bottom.</summary>
    public TouchRect Detail { get; }

    /// <summary>
    /// The scale that fits a card <paramref name="naturalWidth"/> by <paramref name="naturalHeight"/>
    /// canvas units, as its prefab renders unscaled, into <see cref="CardArea"/>.
    /// </summary>
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
