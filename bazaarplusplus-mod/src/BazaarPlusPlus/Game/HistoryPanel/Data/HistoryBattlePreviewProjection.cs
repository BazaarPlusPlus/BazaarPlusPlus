#nullable enable
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

internal static class HistoryBattlePreviewProjection
{
    private static readonly object SocketEffectTemplateLock = new();
    private static readonly Dictionary<
        (Guid TemplateId, int Tier),
        ECardAttributeType?
    > SocketEffectAttributeTypeCache = new();
    private static object? _staticGameData;

    public static BppItemBoard BuildPlayer(PvpBattleSnapshots? snapshots, string signature)
    {
        return Build(snapshots?.PlayerHand, signature);
    }

    public static BppItemBoard BuildOpponent(PvpBattleSnapshots? snapshots, string signature)
    {
        return Build(snapshots?.OpponentHand, signature);
    }

    public static BppItemBoard Build(PvpBattleCardSetCapture? itemCapture, string signature)
    {
        return new BppItemBoard(
            BppItemBoardId.Historical,
            BppItemBoardType.Reference,
            BuildItemBoardCards(itemCapture?.Items),
            signature
        );
    }

    // The static-data question History's snapshot counts ask (HistoryBattleSnapshotCounter), so
    // a row's chip totals match what Build() renders.
    public static bool HasCardTemplate(Guid templateId) =>
        HasStaticCardTemplate(TryGetStaticGameData(), templateId);

    private static List<BppItemBoardCard> BuildItemBoardCards(
        IList<PvpBattleCardSnapshot>? itemSnapshots
    )
    {
        var specs = new List<BppItemBoardCard>();
        if (itemSnapshots == null || itemSnapshots.Count == 0)
            return specs;

        var socketEffectsBySocket = BuildSocketEffectMap(itemSnapshots);
        var staticData = TryGetStaticGameData();

        foreach (
            var snapshot in itemSnapshots
                .Select((snapshot, index) => new { snapshot, index })
                .OrderBy(entry => entry.snapshot?.Socket.HasValue == true ? 0 : 1)
                .ThenBy(entry =>
                    entry.snapshot?.Socket is { } socket
                        ? CardFacetOrder.SocketRank(socket)
                        : int.MaxValue
                )
                .ThenBy(entry => entry.index)
                .Select(entry => entry.snapshot)
        )
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;
            if (snapshot.Type != CardType.Item)
                continue;
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;
            if (!HasStaticCardTemplate(staticData, templateId))
                continue;

            // A card the game gave no size still renders as one socket (ADR-0017).
            var size = snapshot.Size ?? CardSize.Small;
            var attributes = BuildAttributeDictionary(snapshot);
            ApplySocketEffectAttributes(snapshot, attributes, socketEffectsBySocket);

            specs.Add(
                new BppItemBoardCard
                {
                    TemplateId = templateId,
                    InstanceId = snapshot.InstanceId ?? string.Empty,
                    Order = specs.Count,
                    Tier = DesktopCardFacets.Tier.TryToFacet(ParseTier(snapshot.Tier), out var tier)
                        ? tier
                        : CardTier.Bronze,
                    Size = size,
                    Span = BppItemBoardSpan.Resolve(size),
                    EnchantmentType =
                        ParseEnchantmentType(snapshot.Enchant) is { } enchantment
                        && DesktopCardFacets.Enchantment.TryToFacet(enchantment, out var facet)
                            ? facet
                            : null,
                    SourceSocketId = snapshot.Socket is { } socket
                        ? CardFacetOrder.SocketRank(socket)
                        : null,
                    Attributes = attributes.ToDictionary(
                        pair => pair.Key.ToString(),
                        pair => pair.Value
                    ),
                }
            );
        }

        return specs;
    }

    private static Dictionary<ECardAttributeType, int> BuildAttributeDictionary(
        PvpBattleCardSnapshot snapshot
    )
    {
        var attributes = new Dictionary<ECardAttributeType, int>();
        if (snapshot.Attributes == null)
            return attributes;

        foreach (var pair in snapshot.Attributes)
        {
            if (
                Enum.TryParse<ECardAttributeType>(
                    pair.Key,
                    ignoreCase: false,
                    out var attributeType
                )
            )
            {
                attributes[attributeType] = pair.Value;
            }
        }

        return attributes;
    }

    private static IReadOnlyDictionary<
        CardSocket,
        HashSet<ECardAttributeType>
    > BuildSocketEffectMap(IEnumerable<PvpBattleCardSnapshot>? snapshots)
    {
        var result = new Dictionary<CardSocket, HashSet<ECardAttributeType>>();
        if (snapshots == null)
            return result;

        foreach (var snapshot in snapshots)
        {
            if (
                snapshot == null
                || snapshot.Type != CardType.SocketEffect
                || !snapshot.Socket.HasValue
                || string.IsNullOrWhiteSpace(snapshot.TemplateId)
            )
                continue;

            var effectType = ResolveSocketEffectAttributeType(snapshot);
            if (!effectType.HasValue)
                continue;

            if (!result.TryGetValue(snapshot.Socket.Value, out var effects))
            {
                effects = new HashSet<ECardAttributeType>();
                result[snapshot.Socket.Value] = effects;
            }

            effects.Add(effectType.Value);
        }

        return result;
    }

    private static void ApplySocketEffectAttributes(
        PvpBattleCardSnapshot snapshot,
        IDictionary<ECardAttributeType, int> attributes,
        IReadOnlyDictionary<CardSocket, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        if (
            snapshot == null
            || attributes == null
            || socketEffectsBySocket == null
            || socketEffectsBySocket.Count == 0
            || !snapshot.Socket.HasValue
        )
            return;

        foreach (var socket in EnumerateOccupiedSockets(snapshot.Socket.Value, snapshot.Size))
        {
            if (!socketEffectsBySocket.TryGetValue(socket, out var effectTypes))
                continue;

            foreach (var effectType in effectTypes)
            {
                if (!attributes.TryGetValue(effectType, out var currentValue) || currentValue <= 0)
                    attributes[effectType] = 1;
            }
        }
    }

    // The sockets a card covers from its left socket, in board order: one for a card the game
    // gave no size, as a small card.
    private static IEnumerable<CardSocket> EnumerateOccupiedSockets(
        CardSocket startSocket,
        CardSize? size
    )
    {
        var span = size switch
        {
            CardSize.Medium => 2,
            CardSize.Large => 3,
            _ => 1,
        };
        var start = CardFacetOrder.SocketRank(startSocket);
        return BoardSockets.Where(socket =>
        {
            var rank = CardFacetOrder.SocketRank(socket);
            return rank >= start && rank < start + span;
        });
    }

    private static readonly CardSocket[] BoardSockets = (CardSocket[])
        Enum.GetValues(typeof(CardSocket));

    private static ECardAttributeType? ResolveSocketEffectAttributeType(
        PvpBattleCardSnapshot snapshot
    )
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
            return null;

        if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
            return null;

        var tier = (int)ParseTier(snapshot.Tier);
        var cacheKey = (templateId, tier);
        lock (SocketEffectTemplateLock)
        {
            if (SocketEffectAttributeTypeCache.TryGetValue(cacheKey, out var cachedType))
                return cachedType;
        }

        ECardAttributeType? resolvedType = null;
        try
        {
            var staticData = GetStaticGameData();
            var template = GetTemplate(staticData, templateId) as TCardSocketEffect;
            if (template != null)
            {
                var auras = template.GetAuraTemplatesByTier((ETier)Math.Max(0, tier));
                foreach (var aura in auras)
                {
                    if (
                        aura?.Action is TAuraActionCardModifyAttribute action
                        && (
                            action.AttributeType == ECardAttributeType.Heated
                            || action.AttributeType == ECardAttributeType.Chilled
                        )
                    )
                    {
                        resolvedType = action.AttributeType;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.preview.socket_effect_degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("template_id", snapshot.TemplateId),
                ("reason_code", HistoryPanelPreviewReasonCode.SocketEffectLookupFailed)
            );
        }

        lock (SocketEffectTemplateLock)
            SocketEffectAttributeTypeCache[cacheKey] = resolvedType;

        return resolvedType;
    }

    private static bool HasStaticCardTemplate(object? staticData, Guid templateId)
    {
        return templateId != Guid.Empty && GetTemplate(staticData, templateId) != null;
    }

    private static object? TryGetStaticGameData()
    {
        try
        {
            return GetStaticGameData();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.preview.static_data_degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", HistoryPanelPreviewReasonCode.StaticDataAccessFailed)
            );
            return null;
        }
    }

    private static object? GetStaticGameData()
    {
        lock (SocketEffectTemplateLock)
        {
            if (_staticGameData != null)
                return _staticGameData;
        }

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.preview.static_data_degraded",
                    storm: ["reason_code"]
                ),
                ("reason_code", HistoryPanelPreviewReasonCode.StaticDataUnavailable)
            );
            return null;
        }

        lock (SocketEffectTemplateLock)
        {
            _staticGameData ??= staticData;
            return _staticGameData;
        }
    }

    private static object? GetTemplate(object? staticData, Guid templateId)
    {
        return BppStaticDataAccess.GetCardTemplate(staticData, templateId);
    }

    private static ETier ParseTier(string? value)
    {
        return
            !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<ETier>(value, ignoreCase: false, out var tier)
            ? tier
            : ETier.Bronze;
    }

    private static EEnchantmentType? ParseEnchantmentType(string? value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        return Enum.TryParse<EEnchantmentType>(value, ignoreCase: false, out var enchant)
            ? enchant
            : null;
    }
}
