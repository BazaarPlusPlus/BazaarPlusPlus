#nullable enable

namespace BazaarPlusPlus.Core.Cards.LiveCards;

// Sockets are board indices and attribute keys are game member names (ADR-0017).
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
