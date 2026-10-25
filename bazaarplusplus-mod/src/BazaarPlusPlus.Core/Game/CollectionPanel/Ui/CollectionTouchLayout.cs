#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Grid;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

/// <summary>
/// The Collection panel's geometry on a landscape touch screen, in canvas units, top-down from
/// the canvas's top-left corner: a full sheet inside the safe area (<see cref="TouchChrome"/>).
/// The header row carries the title, the Items and Skills tabs, the search field, and Close; under
/// it the filter rail (a drag-scrolled column of chips) stands left of the card grid. The grid's
/// column count follows its width (<see cref="GridColumns"/>), so a card keeps a recognisable
/// size on a phone instead of the desktop's fixed ten-column display case. Every target is at
/// least <see cref="TouchMetrics.MinTargetDp"/> and every text at least
/// <see cref="TouchMetrics.MinBodySp"/>. The inspection is the shared tap-to-inspect sheet; its
/// detail is one drag-scrolled column of tags, preview values and effects,
/// and an expandable enchantment effect list. The desktop's Collection geometry stays on
/// <see cref="CollectionGridConstants"/> and is not derived from this one.
/// </summary>
public sealed class CollectionTouchLayout
{
    /// <summary>The narrowest a small item's column gets, with room for its name beneath the art.</summary>
    public const float MinItemColumnDp = 88f;

    /// <summary>The narrowest a skill's square gets.</summary>
    public const float MinSkillDp = 88f;

    /// <summary>The rail's chip grid; a chip spans two, three, or all six columns.</summary>
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

    /// <summary>The frame, type scale, and close button every touch panel shares.</summary>
    public TouchChrome Chrome { get; }

    public float Gap { get; }

    /// <summary>The whole panel: the safe area less the shared margin.</summary>
    public TouchRect Sheet { get; }

    public TouchRect Title { get; }

    public TouchRect ItemsTab { get; }

    public TouchRect SkillsTab { get; }

    /// <summary>The search field, between the tabs and Close.</summary>
    public TouchRect Search { get; }

    public TouchRect Close { get; }

    /// <summary>The filter rail's drag-scrolled viewport.</summary>
    public TouchRect Rail { get; }

    /// <summary>The card grid's drag-scrolled viewport.</summary>
    public TouchRect Grid { get; }

    /// <summary>A filter or inspection chip's height: one touch target.</summary>
    public float ChipHeight { get; }

    /// <summary>A rail section header's height (hero, tier, keyword, tag, source).</summary>
    public float RailHeaderHeight { get; }

    /// <summary>The tap-to-inspect sheet.</summary>
    public TouchInspectLayout Inspect { get; }

    /// <summary>
    /// The grid's column count for a viewport <paramref name="viewportWidth"/> canvas units wide:
    /// as many as keep a small item's column at least <see cref="MinItemColumnDp"/>, or a skill at
    /// least <see cref="MinSkillDp"/>, up to the desktop's count, and never fewer than a large
    /// item needs.
    /// </summary>
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

    /// <summary>
    /// The scale that fits a card <paramref name="naturalWidth"/> by <paramref name="naturalHeight"/>
    /// canvas units, as its prefab renders unscaled, into the inspection's card area.
    /// </summary>
    public float InspectScale(float naturalWidth, float naturalHeight) =>
        Inspect.Scale(naturalWidth, naturalHeight);
}
