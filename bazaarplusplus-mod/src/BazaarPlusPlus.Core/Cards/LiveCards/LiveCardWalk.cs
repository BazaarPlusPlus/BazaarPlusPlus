#nullable enable

namespace BazaarPlusPlus.Core.Cards.LiveCards;

/// <summary>
/// A host's raw read of the current run in card facets (ADR-0015), before
/// <see cref="LiveCardSnapshotRead"/> checks each card's socket, applies the fallbacks, numbers
/// the cards, and reports the ones it leaves out (ADR-0017). A null hero is a hero with no facet,
/// which reads as no run. The shop is in offer order and its sockets are ignored; the board and
/// the stash are in the order the host yields them, misfits included.
/// </summary>
public sealed record LiveCardWalk(
    CardHero? Hero,
    IReadOnlyList<LiveCardRead> Shop,
    IReadOnlyList<LiveCardRead> Board,
    IReadOnlyList<LiveCardRead> Stash
);

/// <summary>
/// One item card as a host read it. A null tier, size, or enchantment is one the facets cannot
/// name; the socket is the card's left-edge board index, null when the game gave none (ADR-0017).
/// Attributes are keyed by the game's member name. Only managed values: no game object escapes the
/// walk.
/// </summary>
public sealed record LiveCardRead(
    string? InstanceId,
    Guid TemplateId,
    CardTier? Tier,
    CardSize? Size,
    CardEnchantment? Enchantment,
    int? Socket,
    IReadOnlyDictionary<string, int>? Attributes
);
