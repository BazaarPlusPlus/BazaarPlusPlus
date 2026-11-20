#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

/// <summary>The game's recorded board applies a socket effect to every item covering its socket instead of displaying the effect as a card (ADR-0017).</summary>
public static class HistoryBoardProjection
{
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
