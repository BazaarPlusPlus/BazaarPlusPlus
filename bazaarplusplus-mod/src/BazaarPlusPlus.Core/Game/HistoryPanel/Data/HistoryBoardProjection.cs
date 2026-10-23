#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>
/// A battle's stored boards as cards a host renders from the game's own prefabs: the facet-only
/// half of the desktop's <c>HistoryBattlePreviewProjection</c>, for a host that rents native cards
/// by template, tier, and enchantment. A card is kept on the same terms
/// <see cref="HistoryBattleSnapshotCounter"/> counts it: a parseable template id, the capture's
/// card type, and a template the host's static data knows. Items keep the desktop's order (placed
/// cards by socket, then the rest in capture order), a card with no size renders as one socket
/// (ADR-0017), and the board is planned (<see cref="BppItemBoardSlotPlanner"/>) so every card has
/// its display socket. Attributes and socket effects stay with the desktop, whose native board
/// shows them; a rented card shows its template's own values.
/// </summary>
public static class HistoryBoardProjection
{
    /// <summary>The planned item board of one hand.</summary>
    public static BppItemBoard Items(
        PvpBattleCardSetCapture? hand,
        Func<Guid, bool> hasCardTemplate,
        string signature
    )
    {
        if (hasCardTemplate == null)
            throw new ArgumentNullException(nameof(hasCardTemplate));
        var cards = new List<BppItemBoardCard>();
        var ordered = (hand?.Items ?? Array.Empty<PvpBattleCardSnapshot>())
            .Select((snapshot, index) => (Snapshot: snapshot, Index: index))
            .OrderBy(entry => entry.Snapshot?.Socket.HasValue == true ? 0 : 1)
            .ThenBy(entry =>
                entry.Snapshot?.Socket is { } socket
                    ? CardFacetOrder.SocketRank(socket)
                    : int.MaxValue
            )
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Snapshot);
        foreach (var snapshot in ordered)
        {
            if (!TryTemplate(snapshot, CardType.Item, hasCardTemplate, out var templateId))
                continue;
            var size = snapshot!.Size ?? CardSize.Small;
            cards.Add(
                new BppItemBoardCard
                {
                    TemplateId = templateId,
                    InstanceId = snapshot.InstanceId ?? string.Empty,
                    Order = cards.Count,
                    Tier = Tier(snapshot.Tier),
                    Size = size,
                    Span = BppItemBoardSpan.Resolve(size),
                    EnchantmentType = Enchantment(snapshot.Enchant),
                    SourceSocketId = snapshot.Socket is { } socket
                        ? CardFacetOrder.SocketRank(socket)
                        : null,
                }
            );
        }
        return BppItemBoardSlotPlanner.Plan(
            new BppItemBoard(
                BppItemBoardId.Historical,
                BppItemBoardType.Reference,
                cards,
                signature
            )
        );
    }

    /// <summary>One skill set's skills, in capture order.</summary>
    public static IReadOnlyList<BppItemBoardCard> Skills(
        PvpBattleCardSetCapture? skills,
        Func<Guid, bool> hasCardTemplate
    )
    {
        if (hasCardTemplate == null)
            throw new ArgumentNullException(nameof(hasCardTemplate));
        var cards = new List<BppItemBoardCard>();
        foreach (var snapshot in skills?.Items ?? Array.Empty<PvpBattleCardSnapshot>())
        {
            if (!TryTemplate(snapshot, CardType.Skill, hasCardTemplate, out var templateId))
                continue;
            cards.Add(
                new BppItemBoardCard
                {
                    TemplateId = templateId,
                    InstanceId = snapshot!.InstanceId ?? string.Empty,
                    Order = cards.Count,
                    Tier = Tier(snapshot.Tier),
                    Size = CardSize.Medium,
                    Span = 1,
                }
            );
        }
        return cards;
    }

    /// <summary>
    /// The facts a board reports from its rented cards, which Core's
    /// <see cref="HistoryPanelViewModelBuilder.ArchiveStatus"/> turns into its message: loading
    /// while any card is, empty when the board has no card, failed when every card failed, partial
    /// when some did, and ready otherwise.
    /// </summary>
    public static HistoryBoardFacts Facts(int cards, int pending, int failed) =>
        pending > 0 ? new HistoryBoardFacts(NativeMonsterBoardStatus.Loading, 0)
        : cards <= 0 ? new HistoryBoardFacts(NativeMonsterBoardStatus.Empty, 0)
        : failed >= cards ? new HistoryBoardFacts(NativeMonsterBoardStatus.Failed, failed)
        : failed > 0 ? new HistoryBoardFacts(NativeMonsterBoardStatus.Partial, failed)
        : new HistoryBoardFacts(NativeMonsterBoardStatus.Ready, 0);

    private static bool TryTemplate(
        PvpBattleCardSnapshot? snapshot,
        CardType type,
        Func<Guid, bool> hasCardTemplate,
        out Guid templateId
    )
    {
        templateId = Guid.Empty;
        return snapshot != null
            && snapshot.Type == type
            && !string.IsNullOrWhiteSpace(snapshot.TemplateId)
            && Guid.TryParse(snapshot.TemplateId, out templateId)
            && templateId != Guid.Empty
            && hasCardTemplate(templateId);
    }

    private static CardTier Tier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse<CardTier>(value, ignoreCase: false, out var tier)
        && Enum.IsDefined(typeof(CardTier), tier)
            ? tier
            : CardTier.Bronze;

    private static CardEnchantment? Enchantment(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
            ? null
        : Enum.TryParse<CardEnchantment>(value, ignoreCase: false, out var enchantment)
        && Enum.IsDefined(typeof(CardEnchantment), enchantment)
            ? enchantment
        : null;
}
