#nullable enable

namespace BazaarPlusPlus.Core.Cards.ItemBoards;

// Sockets are positional indices on the ten-socket board. Attribute keys are game member names.
public sealed class BppItemBoardCard
{
    public Guid TemplateId { get; init; }

    public string InstanceId { get; init; } = string.Empty;

    public int Order { get; init; }

    public CardTier Tier { get; init; } = CardTier.Bronze;

    public CardSize Size { get; init; } = CardSize.Small;

    public int Span { get; init; }

    public CardEnchantment? EnchantmentType { get; init; }

    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>();

    public int? SourceSocketId { get; init; }

    public int? DisplaySocketId { get; init; }

    public int DisplaySpan => BppItemBoardSpan.Resolve(Size, Span);

    public BppItemBoardCard WithDisplaySocket(int displaySocketId) =>
        new()
        {
            TemplateId = TemplateId,
            InstanceId = InstanceId,
            Order = Order,
            Tier = Tier,
            Size = Size,
            Span = Span,
            EnchantmentType = EnchantmentType,
            Attributes =
                Attributes != null
                    ? Attributes.ToDictionary(pair => pair.Key, pair => pair.Value)
                    : new Dictionary<string, int>(),
            SourceSocketId = SourceSocketId,
            DisplaySocketId = displaySocketId,
        };
}
