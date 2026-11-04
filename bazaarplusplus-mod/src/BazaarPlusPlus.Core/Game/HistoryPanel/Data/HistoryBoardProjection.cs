#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>
/// A battle's stored boards as cards a host renders from the game's own prefabs, on every host:
/// the desktop's native board and the touch boards. A card is kept on the same terms
/// <see cref="HistoryBattleSnapshotCounter"/> counts it: a parseable template id, the capture's
/// card type, and a template the host's static data knows. Items keep the desktop's order (placed
/// cards by socket, then the rest in capture order), a card with no size renders as one socket
/// (ADR-0017), and the board is planned (<see cref="BppItemBoardSlotPlanner"/>) so every card has
/// its display socket. Each card carries its Saved Card Values, which the rendered card shows over
/// its template's. A socket effect in the hand is no card on the board: it raises the attribute
/// its template sets (Heated or Chilled, named by the host) to at least 1 on every placed item
/// that covers its socket, as the game shows a recorded board (ADR-0017, amended 2026-11-04).
/// </summary>
public static class HistoryBoardProjection
{
    /// <summary>
    /// The planned item board of one hand. <paramref name="socketEffectAttribute"/> names the
    /// attribute a socket-effect template sets at a tier, or null for none
    /// (<see cref="IHistoryCardTemplates.SocketEffectAttribute"/>).
    /// </summary>
    public static BppItemBoard Items(
        PvpBattleCardSetCapture? hand,
        Func<Guid, bool> hasCardTemplate,
        Func<Guid, CardTier, string?> socketEffectAttribute,
        string signature
    )
    {
        if (hasCardTemplate == null)
            throw new ArgumentNullException(nameof(hasCardTemplate));
        if (socketEffectAttribute == null)
            throw new ArgumentNullException(nameof(socketEffectAttribute));
        var socketEffects = SocketEffects(hand, socketEffectAttribute);
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
            var span = BppItemBoardSpan.Resolve(size);
            int? sourceSocket = snapshot.Socket is { } socket
                ? CardFacetOrder.SocketRank(socket)
                : null;
            var attributes = Attributes(snapshot);
            if (sourceSocket is { } start)
                for (var rank = start; rank < start + span; rank++)
                {
                    if (!socketEffects.TryGetValue(rank, out var names))
                        continue;
                    foreach (var name in names)
                        if (!attributes.TryGetValue(name, out var value) || value <= 0)
                            attributes[name] = 1;
                }
            cards.Add(
                new BppItemBoardCard
                {
                    TemplateId = templateId,
                    InstanceId = snapshot.InstanceId ?? string.Empty,
                    Order = cards.Count,
                    Tier = Tier(snapshot.Tier),
                    Size = size,
                    Span = span,
                    EnchantmentType = Enchantment(snapshot.Enchant),
                    SourceSocketId = sourceSocket,
                    Attributes = attributes,
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
                    Attributes = Attributes(snapshot),
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

    private static Dictionary<string, int> Attributes(PvpBattleCardSnapshot snapshot) =>
        snapshot.Attributes != null
            ? new Dictionary<string, int>(snapshot.Attributes)
            : new Dictionary<string, int>();

    // The attribute names the hand's socket effects set, by socket index.
    private static Dictionary<int, HashSet<string>> SocketEffects(
        PvpBattleCardSetCapture? hand,
        Func<Guid, CardTier, string?> socketEffectAttribute
    )
    {
        var effects = new Dictionary<int, HashSet<string>>();
        foreach (var snapshot in hand?.Items ?? Array.Empty<PvpBattleCardSnapshot>())
        {
            if (
                snapshot == null
                || snapshot.Type != CardType.SocketEffect
                || snapshot.Socket is not { } socket
                || !Guid.TryParse(snapshot.TemplateId, out var templateId)
                || socketEffectAttribute(templateId, Tier(snapshot.Tier)) is not { } name
            )
                continue;
            var rank = CardFacetOrder.SocketRank(socket);
            if (!effects.TryGetValue(rank, out var names))
                effects[rank] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(name);
        }
        return effects;
    }

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
