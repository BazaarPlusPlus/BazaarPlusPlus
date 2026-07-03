#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure.UiTokens;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Fixed "display-case" grid spec. The catalog is laid out on a fixed per-tab column count
// regardless of viewport width: screen width only changes the base unit size and horizontal
// centering, never the column count. Items use ItemColumns unit columns (span 1/2/3 wide by
// ECardSize, ItemRowSpan tall); Skills use fewer, larger SkillColumns square icons. See the
// design doc §17 for the redesign that superseded the old dynamic min/max column model.
internal static class CollectionGridConstants
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
    // fills the available width, then clamped to [Min, Max*]; any extra width is absorbed as
    // centering margin rather than more columns. The per-tab max caps how large cells grow on
    // very wide screens (Skills are allowed roughly twice the Item cap so icons read large).
    // MinUnitWidth is only a degenerate floor: the columns are fixed, so on a narrow viewport
    // the cells must shrink to stay inside the clip (there is no horizontal scroll) — keep it
    // small enough that a real game window never clamps up and overflows the rightmost columns.
    public const float GridGap = 14f;
    public const float GridOuterPadding = 18f;
    public const float MinUnitWidth = 24f;
    public const float ItemMaxUnitWidth = 172f;
    public const float SkillMaxUnitWidth = 272f;

    // Fraction of a cell kept as breathing room on every side so the native card sits inside
    // its slot (the slot background then reads as a frame around it) instead of touching edges.
    public const float CellContentInset = 0.06f;

    public const int RowOverscan = 1;

    // Per-frame wall-clock budget for cold cell binds (cards entering window for the first
    // time). Reposition is free; bind triggers Addressables loads on the Item path.
    public const float ColdBindBudgetMs = 3f;
    public const float CardFadeInSeconds = 0.06f;

    // Sorting layers: UITK panel below native cards, with optional foreground chrome above.
    // Anything we put between them (e.g., a full-screen GraphicRaycaster blocker) intercepts
    // every click and wheel event before UITK can see it, freezing all panel chrome — so we don't.
    public const int UiToolkitSortingOrder = BppOverlaySorting.PanelUiToolkit;
    public const int OverlaySortingOrder = BppOverlaySorting.NativeCardPreview;

    // true (default): polled hover. No per-card hit Image, no overlay GraphicRaycaster;
    // Mouse.current is polled each Update to dispatch OnHover/OnHoverOut on the cell under
    // the cursor. The lower UITK panel receives every click and wheel event uninterrupted.
    //
    // false: raycaster hover. Per-card transparent Image + overlay GraphicRaycaster fire
    // hover via IPointerEnter/Exit. The card prefab's own RawImage has raycastTarget=true,
    // so the overlay raycaster catches wheel events landing on cards and drops them —
    // ScrollView wheel scrolling stops working in the card region. Only flip for diagnostic
    // work; a real fix would also need an IScrollHandler forwarder on the overlay root.
    //
    // `static readonly` (not `const`) so the unused branch does not dead-code-warn when the
    // flag is flipped in source.
    public static readonly bool UsePolledHover = true;

    // Open is a presentation (deliberate); close is a dismissal (snappy). With out at 0.04
    // the close-fade visually settles in ~120ms, fast enough not to feel like the panel
    // is "lingering" after Escape / F9, but still smooth enough to avoid a hard pop.
    public const float PanelFadeInSeconds = 0.12f;
    public const float PanelFadeOutSeconds = 0.04f;

    // UITK points scrolled per mouse-wheel notch. The previous smooth-wheel intercept
    // tried to lerp scrollOffset itself but the WheelEvent interaction with ScrollView's
    // default handler killed wheel scrolling entirely on this UITK version; we ship the
    // built-in instant snap instead and just give each notch enough travel to feel meaty.
    public const float MouseWheelScrollPoints = 120f;

    // Unit width an item card occupies on the item grid. Skills never call this (always 1).
    public static int ItemWidthSpan(ECardSize size) =>
        size switch
        {
            ECardSize.Large => ItemLargeSpan,
            ECardSize.Medium => ItemMediumSpan,
            _ => ItemSmallSpan,
        };

    // Cell height in row-units: Items are two units tall (uniform height, width varies by
    // size); Skills are a single square unit.
    public static int RowSpanFor(ECardType type) =>
        type == ECardType.Skill ? SkillRowSpan : ItemRowSpan;

    public static int ColumnsFor(ECardType type) =>
        type == ECardType.Skill ? SkillColumns : ItemColumns;

    public static float MaxUnitWidthFor(ECardType type) =>
        type == ECardType.Skill ? SkillMaxUnitWidth : ItemMaxUnitWidth;
}
