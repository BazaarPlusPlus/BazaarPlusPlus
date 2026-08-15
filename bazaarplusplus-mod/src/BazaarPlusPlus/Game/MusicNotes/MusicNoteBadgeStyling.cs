#nullable enable
namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>How the card under the pointer relates to a socket's note gate.</summary>
internal enum MusicNoteHoverFit
{
    /// <summary>No card hovered, or the socket's gate is unknown (catalog still loading).</summary>
    None,

    /// <summary>The hovered card satisfies the gate and can actually cover this socket
    /// (enough contiguous free room for its size; its own current span counts as free).</summary>
    Fits,

    /// <summary>The hovered card satisfies the gate, but other items block every placement
    /// that would cover this socket.</summary>
    FitsBlocked,

    /// <summary>The hovered card fails this socket's note gate.</summary>
    Misses,
}

/// <summary>Resolved visual for one badge; the overlay maps each to concrete colors.</summary>
internal enum MusicNoteBadgeVisual
{
    /// <summary>Implied letter, no hover filter: dim neutral chip.</summary>
    Ghost,

    /// <summary>Placed note, no hover filter: soft accent plate.</summary>
    Plate,

    /// <summary>Placed note whose occupant passes the gate: category glow over the plate.</summary>
    Boosted,

    /// <summary>Implied letter the hovered card fits and can reach: gold glow.</summary>
    GhostHighlighted,

    /// <summary>Placed note the hovered card fits and can reach: gold glow.</summary>
    PlateHighlighted,

    /// <summary>Implied letter that matches the hovered card but is blocked: gold letter,
    /// no glow.</summary>
    GhostMatchBlocked,

    /// <summary>Placed note that matches the hovered card but is blocked: gold letter and
    /// trim, no glow.</summary>
    PlateMatchBlocked,

    /// <summary>Implied letter the hovered card misses: desaturated.</summary>
    GhostDimmed,

    /// <summary>Placed note the hovered card misses: desaturated.</summary>
    PlateDimmed,
}

/// <summary>
/// Pure badge-state resolution: combines note presence, live boost, and the hover filter into
/// one visual. Boosted is hover-immune — an actually-working note never dims or restyles under
/// a hover comparison, so live board state always outranks the what-if layer. The hover layer
/// itself is three-valued: glow means "works and can go here", gold-without-glow means "the
/// letter matches but the spot is blocked", desaturation means "does not work". This file
/// stays free of Unity/game types so the precedence rules are testable in a plain test
/// project.
/// </summary>
internal static class MusicNoteBadgeStyling
{
    internal static MusicNoteBadgeVisual Resolve(
        bool notePlaced,
        bool isBoosted,
        MusicNoteHoverFit hoverFit
    )
    {
        if (notePlaced && isBoosted)
            return MusicNoteBadgeVisual.Boosted;

        return hoverFit switch
        {
            MusicNoteHoverFit.Fits => notePlaced
                ? MusicNoteBadgeVisual.PlateHighlighted
                : MusicNoteBadgeVisual.GhostHighlighted,
            MusicNoteHoverFit.FitsBlocked => notePlaced
                ? MusicNoteBadgeVisual.PlateMatchBlocked
                : MusicNoteBadgeVisual.GhostMatchBlocked,
            MusicNoteHoverFit.Misses => notePlaced
                ? MusicNoteBadgeVisual.PlateDimmed
                : MusicNoteBadgeVisual.GhostDimmed,
            _ => notePlaced ? MusicNoteBadgeVisual.Plate : MusicNoteBadgeVisual.Ghost,
        };
    }
}
