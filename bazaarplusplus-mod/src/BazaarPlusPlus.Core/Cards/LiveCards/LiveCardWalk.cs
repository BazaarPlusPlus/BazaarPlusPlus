#nullable enable

namespace BazaarPlusPlus.Core.Cards.LiveCards;

// Shop entries have no meaningful socket. Preserve offer order and ignore their socket values.
public sealed record LiveCardWalk(
    CardHero? Hero,
    IReadOnlyList<LiveCardRead> Shop,
    IReadOnlyList<LiveCardRead> Board,
    IReadOnlyList<LiveCardRead> Stash
);

// Attribute keys are game member names. Only managed values may leave the host walk.
public sealed record LiveCardRead(
    string? InstanceId,
    Guid TemplateId,
    CardTier? Tier,
    CardSize? Size,
    CardEnchantment? Enchantment,
    int? Socket,
    IReadOnlyDictionary<string, int>? Attributes
);
