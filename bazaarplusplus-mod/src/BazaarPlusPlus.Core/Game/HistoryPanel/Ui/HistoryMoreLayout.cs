namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

// Text heights are measured by the host at each column's width; all exposed rects use
// the same normalized, top-down coordinates as HistoryPanelLayout.
public sealed class HistoryMoreLayout
{
    public const float Width = 720;
    public const float Padding = 28;
    public const float ContentWidth = Width - 2 * Padding;
    public const float ControlHeight = 42;
    public const float ActionWidth = 160;
    public const float LabelWidth = ContentWidth - ActionWidth - 12;

    public HistoryMoreLayout(
        float statusHeight,
        float accountStatusHeight,
        float guidanceHeight,
        bool actionsVisible,
        bool formVisible,
        float hintHeight,
        float bannerHeight,
        bool alreadyLinkedVisible = false
    )
    {
        var accountTop = statusHeight > 0 ? 136 + statusHeight + 24 : 152;
        var statusTop = accountTop + 32;
        var guidanceTop = statusTop + accountStatusHeight + 8;
        var actionsBottom =
            accountTop + ControlHeight + (alreadyLinkedVisible ? ControlHeight + 12 : 0);
        var accountBottom = actionsVisible
            ? System.Math.Max(guidanceTop + guidanceHeight, actionsBottom)
            : statusTop + accountStatusHeight;
        var hintTop = accountBottom + 24;
        var inputTop = hintTop + hintHeight + 10;
        var bannerTop = inputTop + ControlHeight + 8;
        var bottom = accountBottom;
        if (formVisible)
            bottom = bannerHeight > 0 ? bannerTop + bannerHeight : inputTop + ControlHeight;
        Height = bottom + Padding;

        Title = Rect(Padding, 24, ContentWidth - 116, ControlHeight);
        Close = Rect(Width - Padding - 104, 24, 104, ControlHeight);
        Database = Rect(Padding, 86, ContentWidth - ActionWidth - 12, ControlHeight);
        Health = Rect(Width - Padding - ActionWidth, 86, ActionWidth, ControlHeight);
        Status = Rect(Padding, 136, ContentWidth, statusHeight);
        var labelWidth = actionsVisible ? LabelWidth : ContentWidth;
        AccountTitle = Rect(Padding, accountTop, labelWidth, 28);
        AccountStatus = Rect(Padding, statusTop, labelWidth, accountStatusHeight);
        Guidance = Rect(Padding, guidanceTop, labelWidth, guidanceHeight);
        Toggle = Rect(Width - Padding - ActionWidth, accountTop, ActionWidth, ControlHeight);
        AlreadyLinked = Rect(
            Width - Padding - ActionWidth,
            accountTop + ControlHeight + 12,
            ActionWidth,
            ControlHeight
        );
        Hint = Rect(Padding, hintTop, ContentWidth, hintHeight);
        Input = Rect(Padding, inputTop, ContentWidth - ActionWidth - 12, ControlHeight);
        Submit = Rect(Width - Padding - ActionWidth, inputTop, ActionWidth, ControlHeight);
        Banner = Rect(Padding, bannerTop, ContentWidth, bannerHeight);
    }

    public float Height { get; }
    public HistoryRect Title { get; }
    public HistoryRect Close { get; }
    public HistoryRect Database { get; }
    public HistoryRect Health { get; }
    public HistoryRect Status { get; }
    public HistoryRect AccountTitle { get; }
    public HistoryRect AccountStatus { get; }
    public HistoryRect Guidance { get; }
    public HistoryRect Toggle { get; }
    public HistoryRect AlreadyLinked { get; }
    public HistoryRect Hint { get; }
    public HistoryRect Input { get; }
    public HistoryRect Submit { get; }
    public HistoryRect Banner { get; }

    public float FitScale(
        float availableWidth,
        float availableHeight,
        float? referenceHeight = null
    ) =>
        System.Math.Min(
            1,
            System.Math.Min(availableWidth / Width, availableHeight / (referenceHeight ?? Height))
        );

    private HistoryRect Rect(float x, float y, float width, float height) =>
        new(x / Width, y / Height, width / Width, height / Height);
}
