#nullable enable

namespace BazaarPlusPlus.Core.Cards.LiveCards;

/// <summary>
/// One item card the player holds or is offered, read by a host's live reader and mapped to card
/// facets (ADR-0015). The socket is a board index 0..9 and attributes are keyed by the game's
/// member name (ADR-0017).
/// </summary>
public sealed class LiveCardSnapshot
{
    public string InstanceId { get; init; } = string.Empty;

    public Guid TemplateId { get; init; }

    public int Order { get; init; }

    public CardTier Tier { get; init; } = CardTier.Bronze;

    public CardSize Size { get; init; } = CardSize.Small;

    public CardEnchantment? EnchantmentType { get; init; }

    public int? SocketId { get; init; }

    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>();
}
