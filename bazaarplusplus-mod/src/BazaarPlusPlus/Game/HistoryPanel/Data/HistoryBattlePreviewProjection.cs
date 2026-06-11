#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

internal static class HistoryBattlePreviewProjection
{
    private static readonly object SocketEffectTemplateLock = new();
    private static readonly Dictionary<
        (Guid TemplateId, int Tier),
        ECardAttributeType?
    > SocketEffectAttributeTypeCache = new();
    private static object? _staticGameData;

    public static HistoryBattlePreviewData BuildEmpty(string signature = "")
    {
        return new HistoryBattlePreviewData(
            new BppItemBoard(
                BppItemBoardId.Historical,
                BppItemBoardType.Reference,
                Array.Empty<BppItemBoardCard>(),
                signature
            ),
            signature
        );
    }

    public static HistoryBattlePreviewData BuildPlayer(
        PvpBattleSnapshots? snapshots,
        string signature
    )
    {
        return Build(snapshots?.PlayerHand, signature);
    }

    public static HistoryBattlePreviewData BuildOpponent(
        PvpBattleSnapshots? snapshots,
        string signature
    )
    {
        return Build(snapshots?.OpponentHand, signature);
    }

    public static HistoryBattlePreviewData Build(
        PvpBattleCardSetCapture? itemCapture,
        string signature
    )
    {
        var board = new BppItemBoard(
            BppItemBoardId.Historical,
            BppItemBoardType.Reference,
            BuildItemBoardCards(itemCapture?.Items),
            signature
        );
        return new HistoryBattlePreviewData(board, signature);
    }

    public static HistoryBattleSnapshotCounts CountSnapshots(
        PvpBattleCardSetCapture? playerHand,
        PvpBattleCardSetCapture? playerSkills,
        PvpBattleCardSetCapture? opponentHand,
        PvpBattleCardSetCapture? opponentSkills
    )
    {
        // Counts match what Build() would render so row-chip totals stay aligned with the
        // preview board: drop snapshots with empty/unparseable TemplateId, type mismatch
        // (e.g. SocketEffect appearing in an item capture), or missing static template.
        var staticData = TryGetStaticGameData();
        return new HistoryBattleSnapshotCounts(
            CountRenderable(playerHand?.Items, isSkill: false, staticData),
            CountRenderable(playerSkills?.Items, isSkill: true, staticData),
            CountRenderable(opponentHand?.Items, isSkill: false, staticData),
            CountRenderable(opponentSkills?.Items, isSkill: true, staticData)
        );
    }

    private static int CountRenderable(
        IList<PvpBattleCardSnapshot>? snapshots,
        bool isSkill,
        object? staticData
    )
    {
        if (snapshots == null)
            return 0;

        var count = 0;
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;
            if (isSkill ? snapshot.Type != ECardType.Skill : snapshot.Type != ECardType.Item)
                continue;
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;
            if (!HasStaticCardTemplate(staticData, templateId))
                continue;
            count++;
        }
        return count;
    }

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
                    entry.snapshot?.Socket.HasValue == true
                        ? (int)entry.snapshot.Socket!.Value
                        : int.MaxValue
                )
                .ThenBy(entry => entry.index)
                .Select(entry => entry.snapshot)
        )
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;
            if (snapshot.Type != ECardType.Item)
                continue;
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;
            if (!HasStaticCardTemplate(staticData, templateId))
                continue;

            var attributes = BuildAttributeDictionary(snapshot);
            ApplySocketEffectAttributes(snapshot, attributes, socketEffectsBySocket);

            specs.Add(
                new BppItemBoardCard
                {
                    TemplateId = templateId,
                    InstanceId = snapshot.InstanceId ?? string.Empty,
                    Order = specs.Count,
                    Tier = ParseTier(snapshot.Tier),
                    Size = snapshot.Size,
                    Span = BppItemBoardSpan.Resolve(snapshot.Size),
                    EnchantmentType = ParseEnchantmentType(snapshot.Enchant),
                    SourceSocketId = snapshot.Socket,
                    Attributes = attributes,
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
        EContainerSocketId,
        HashSet<ECardAttributeType>
    > BuildSocketEffectMap(IEnumerable<PvpBattleCardSnapshot>? snapshots)
    {
        var result = new Dictionary<EContainerSocketId, HashSet<ECardAttributeType>>();
        if (snapshots == null)
            return result;

        foreach (var snapshot in snapshots)
        {
            if (
                snapshot == null
                || snapshot.Type != ECardType.SocketEffect
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
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
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

    private static IEnumerable<EContainerSocketId> EnumerateOccupiedSockets(
        EContainerSocketId startSocket,
        ECardSize size
    )
    {
        var span = size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };
        var start = Math.Max(0, (int)startSocket);
        var end = Math.Min(9, start + Math.Max(1, span) - 1);
        for (var value = start; value <= end; value++)
            yield return (EContainerSocketId)value;
    }

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
            BppLog.Warn(
                "HistoryPanel",
                $"Failed to resolve socket-effect attribute type for {snapshot.TemplateId}: {ex.Message}"
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
            BppLog.Error(
                "HistoryBattlePreviewProjection",
                "Failed to load static game data for battle preview filtering",
                ex
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
            return null;

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
