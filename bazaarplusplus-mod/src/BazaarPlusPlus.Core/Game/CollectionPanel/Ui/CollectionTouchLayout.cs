#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Grid;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

/// <summary>Canvas coordinates start at the top left and increase downward.
/// Size grid columns by the viewport to keep cards recognizable on phones.</summary>
public sealed class CollectionTouchLayout
{
    public const float MinItemColumnDp = 88f;

    public const float MinSkillDp = 88f;

    public const int RailColumns = 6;

    private const float RailShare = 0.3f;
    private const float RailMinDp = 240f;
    private const float RailMaxDp = 340f;
    private const float TabDp = 112f;
    private const float RailHeaderDp = 32f;

    private CollectionTouchLayout(TouchMetrics metrics)
    {
        Metrics = metrics;
        Chrome = TouchChrome.For(metrics);
        var body = Chrome.Body;
        var gap = Chrome.Gap;
        var target = Chrome.Target;
        Gap = gap;

        Sheet = Chrome.Sheet;
        Close = Chrome.Close;
        var railWidth = Math.Min(
            Math.Clamp(body.Width * RailShare, metrics.Dp(RailMinDp), metrics.Dp(RailMaxDp)),
            body.Width * 0.45f
        );
        Title = new TouchRect(body.Left, body.Top, railWidth, target);
        var tabWidth = metrics.Target(TabDp);
        ItemsTab = new TouchRect(Title.Right + gap, body.Top, tabWidth, target);
        SkillsTab = new TouchRect(ItemsTab.Right + gap, body.Top, tabWidth, target);
        Search = new TouchRect(
            SkillsTab.Right + gap,
            body.Top,
            Math.Max(0f, Close.Left - gap - SkillsTab.Right - gap),
            target
        );

        var top = body.Top + target + gap;
        var height = Math.Max(0f, body.Bottom - top);
        Rail = new TouchRect(body.Left, top, railWidth, height);
        Grid = new TouchRect(
            Rail.Right + gap,
            top,
            Math.Max(0f, body.Right - Rail.Right - gap),
            height
        );

        ChipHeight = target;
        RailHeaderHeight = metrics.Dp(RailHeaderDp);
        Inspect = Chrome.Inspect();
    }

    public static CollectionTouchLayout For(TouchMetrics metrics) => new(metrics);

    public TouchMetrics Metrics { get; }

    public TouchChrome Chrome { get; }

    public float Gap { get; }

    public TouchRect Sheet { get; }

    public TouchRect Title { get; }

    public TouchRect ItemsTab { get; }

    public TouchRect SkillsTab { get; }

    public TouchRect Search { get; }

    public TouchRect Close { get; }

    public TouchRect Rail { get; }

    public TouchRect Grid { get; }

    public float ChipHeight { get; }

    public float RailHeaderHeight { get; }

    public TouchInspectLayout Inspect { get; }

    public int GridColumns(CardType type, float viewportWidth)
    {
        var available = viewportWidth - 2f * CollectionGridConstants.GridOuterPadding;
        if (type == CardType.Skill)
            return TouchMetrics.ColumnCount(
                available,
                Metrics.Dp(MinSkillDp),
                CollectionGridConstants.GridGap,
                CollectionGridConstants.SkillColumns
            );
        return Math.Max(
            CollectionGridConstants.ItemLargeSpan,
            TouchMetrics.ColumnCount(
                available,
                Metrics.Dp(MinItemColumnDp),
                CollectionGridConstants.GridGap,
                CollectionGridConstants.ItemColumns
            )
        );
    }

    public float InspectScale(float naturalWidth, float naturalHeight) =>
        Inspect.Scale(naturalWidth, naturalHeight);
}
