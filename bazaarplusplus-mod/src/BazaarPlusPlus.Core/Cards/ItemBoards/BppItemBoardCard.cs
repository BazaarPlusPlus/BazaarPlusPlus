#nullable enable

namespace BazaarPlusPlus.Core.Cards.ItemBoards;

/// <summary>
/// One item on an item board, on card facets (ADR-0015). Sockets are positional indexes on the
/// ten-socket board and attributes are keyed by the game's member name; both carve-outs are
/// ADR-0017. The host's preview mapper converts back to the game's types.
/// </summary>
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

    /// <summary>Socket index 0..9 the card occupies in its source container, if any.</summary>
    public int? SourceSocketId { get; init; }

    /// <summary>Socket index 0..9 the slot planner placed the card at.</summary>
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
