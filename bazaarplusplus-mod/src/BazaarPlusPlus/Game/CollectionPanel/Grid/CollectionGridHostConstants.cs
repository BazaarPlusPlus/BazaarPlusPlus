#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// The desktop grid's host-only knobs: its overlay canvas order, the polled-hover switch, and the
// UI Toolkit wheel step. Geometry and animation timing shared by every host live in Core's
// CollectionGridConstants.
internal static class CollectionGridHostConstants
{
    // Sorting layers: UITK panel below native cards, with optional foreground chrome above.
    // Anything we put between them (e.g., a full-screen GraphicRaycaster blocker) intercepts
    // every click and wheel event before UITK can see it, freezing all panel chrome — so we don't.
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

    // UITK points scrolled per mouse-wheel notch. The previous smooth-wheel intercept
    // tried to lerp scrollOffset itself but the WheelEvent interaction with ScrollView's
    // default handler killed wheel scrolling entirely on this UITK version; we ship the
    // built-in instant snap instead and just give each notch enough travel to feel meaty.
    public const float MouseWheelScrollPoints = 120f;
}
