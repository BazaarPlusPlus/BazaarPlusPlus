#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Fixed "display-case" grid spec. The catalog is laid out on a fixed per-tab column count
// regardless of viewport width: screen width only changes the base unit size and horizontal
// centering, never the column count. Items use ItemColumns unit columns (span 1/2/3 wide by
// CardSize, ItemRowSpan tall); Skills use fewer, larger SkillColumns square icons. See the
// design doc §17 for the redesign that superseded the old dynamic min/max column model.
public static class CollectionGridConstants
{
    // Per-tab column counts. Items use a dense unit grid; Skills are fewer, larger pure icons.
    public const int ItemColumns = 10;
    public const int SkillColumns = 7;
    public const int SkillRowSpan = 1;
    public const int ItemRowSpan = 2;
    public const int ItemSmallSpan = 1;
    public const int ItemMediumSpan = 2;
    public const int ItemLargeSpan = 3;

    // Fixed gutter between cells (both axes) and inner padding of the display-case region, in
    // overlay pixels. The base unit (one column's width) is derived per-viewport so the grid
    // fills the entire available width; the fixed column count stays unchanged on wide screens.
    // MinUnitWidth is only a degenerate floor for an extremely narrow viewport.
    public const float GridGap = 14f;
    public const float GridOuterPadding = 18f;
    public const float MinUnitWidth = 24f;

    // Fraction of a cell kept as breathing room on every side so the native card sits inside
    // its slot (the slot background then reads as a frame around it) instead of touching edges.
    public const float CellContentInset = 0.06f;

    public const int RowOverscan = 1;

    // Per-frame wall-clock budget for cold cell binds (cards entering window for the first
    // time). Reposition is free; bind triggers Addressables loads on the Item path.
    public const float ColdBindBudgetMs = 3f;

    // Animation tuning. All durations are characteristic times for an exponential lerp
    // (t = 1 - exp(-dt / tau)), so the visible motion finishes within ~3*tau seconds.
    public const float CardFadeInSeconds = 0.18f;
    public const float CardHoverScale = 1.2f;
    public const float CardHoverScaleInSeconds = 0.08f;
    public const float CardHoverScaleOutSeconds = 0.1f;
    public const float CardHoverShadowAlpha = 0.2f;
    public const float CardHoverShadowInSeconds = 0.06f;
    public const float CardHoverShadowOutSeconds = 0.1f;
    public const float CardHoverShadowPadding = 10f;
    public const float CardHoverShadowOffsetY = -5f;

    // Open is a presentation (deliberate); close is a dismissal (snappy). With out at 0.04
    // the close-fade visually settles in ~120ms, fast enough not to feel like the panel
    // is "lingering" after Escape / F9, but still smooth enough to avoid a hard pop.
    public const float PanelFadeInSeconds = 0.12f;
    public const float PanelFadeOutSeconds = 0.04f;

    // Unit width an item card occupies on the item grid. Skills never call this (always 1).
    public const float ItemCardWidthScale = 0.96f;

    public static int ItemWidthSpan(CardSize size) =>
        size switch
        {
            CardSize.Large => ItemLargeSpan,
            CardSize.Medium => ItemMediumSpan,
            _ => ItemSmallSpan,
        };

    // Cell height in row-units: Items are two units tall (uniform height, width varies by
    // size); Skills are a single square unit.
    public static int RowSpanFor(CardType type) =>
        type == CardType.Skill ? SkillRowSpan : ItemRowSpan;

    public static int ColumnsFor(CardType type) =>
        type == CardType.Skill ? SkillColumns : ItemColumns;
}
