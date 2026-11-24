#nullable enable
#pragma warning disable CS0436
using System.Runtime.CompilerServices;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarPlusPlus.Core.Events;
using MessagePack;

namespace BazaarPlusPlus.Patches.RunLogging;

// Serialize on the main thread before dispatch because game handlers mutate some messages.
internal static class RunMessageStreamTap
{
    private const string MonsterCombatState = "Combat";

    private sealed record PendingPveOpening(string? EncounterId, int? Day, int? Hour);

    private static readonly ConditionalWeakTable<INetMessage, object> Recorded = new();
    private static readonly ConditionalWeakTable<INetMessage, object> ResponseCommands = new();
    private static readonly object Seen = new();
    private static PendingPveOpening? _pendingPveOpening;
    private static int _lastCommandNumber;

    // The aggregate already contains its child messages, so recording their dispatch duplicates them.
    internal static void OnReceived(INetMessage message)
    {
        if (Recorded.TryGetValue(message, out _))
            return;
        int? respondsTo = ResponseCommands.TryGetValue(message, out var number)
            ? (int)number
            : null;
        Record(message, respondsTo);
    }

    internal static int OnCommand(string method, string argsJson)
    {
        if (method == "SendInitializeRun")
            _pendingPveOpening = null;
        var number = Interlocked.Increment(ref _lastCommandNumber);
        BppPatchHost.Services.EventBus.Publish(
            new RunMessageStreamFrameObserved
            {
                CommandNumber = number,
                Command = method,
                CommandArgsJson = argsJson,
                StartsRun = method == "SendInitializeRun",
            }
        );
        return number;
    }

    // Record here when the response bypasses the dispatch seam.
    internal static void OnResponse(int number, INetMessage response, bool reachesSeam)
    {
        if (reachesSeam)
        {
            ResponseCommands.Remove(response);
            ResponseCommands.Add(response, number);
            return;
        }
        Record(response, number);
    }

    private static void Record(INetMessage message, int? respondsTo)
    {
        Recorded.Remove(message);
        Recorded.Add(message, Seen);
        if (message is NetMessageAggregate aggregate)
        {
            foreach (var child in aggregate.Messages)
                if (child != null)
                    Record(child, respondsTo);
            return;
        }
        if (message is NetMessageCombatSim combatSim)
        {
            // Consume the one-use PvE opening so stale state can never be attributed to another fight.
            var opening = _pendingPveOpening;
            _pendingPveOpening = null;
            if (opening != null && combatSim.Data != null)
            {
                BppPatchHost.Services.EventBus.Publish(
                    new RunMessageStreamFrameObserved
                    {
                        PveCombat = ExtractPveSummary(opening, combatSim.Data),
                    }
                );
            }
            return;
        }
        if (message is NetMessageGameSim gameSim)
        {
            _pendingPveOpening = string.Equals(
                gameSim.Data?.CurrentState?.StateName.ToString(),
                MonsterCombatState,
                StringComparison.Ordinal
            )
                ? new PendingPveOpening(
                    gameSim.Data?.CurrentState?.CurrentEncounterId,
                    gameSim.Data?.Run is { } dayRun ? unchecked((int)dayRun.Day) : null,
                    gameSim.Data?.Run is { } hourRun ? unchecked((int)hourRun.Hour) : null
                )
                : null;
        }
        else if (message is NetMessageRunInitialized or NetMessageGameStateSync)
        {
            _pendingPveOpening = null;
        }
        else if (message is not NetMessageError)
        {
            return;
        }
        BppPatchHost.Services.EventBus.Publish(
            new RunMessageStreamFrameObserved
            {
                Message = MessagePackSerializer.Serialize(message, MessagePackConfig.Options),
                RespondsToCommand = respondsTo,
            }
        );
    }

    private static PveCombatSummary ExtractPveSummary(PendingPveOpening opening, CombatSim combat)
    {
        var goldHits = 0;
        var xpHits = 0;
        var frames = combat.Frames;
        var frameCount = frames?.Count ?? 0;
        if (frames != null)
        {
            for (var f = 0; f < frames.Count; f++)
            {
                var events = frames[f]?.Events;
                if (events == null)
                    continue;
                for (var e = 0; e < events.Count; e++)
                {
                    if (events[e] is CombatSimEventMonsterGoldReceived)
                        goldHits++;
                    else if (events[e] is CombatSimEventMonsterXpReceived)
                        xpHits++;
                }
            }
        }

        Dictionary<string, Dictionary<string, int>>? cardStats = null;
        if (combat.CardStats is { Count: > 0 } rawStats)
        {
            cardStats = new Dictionary<string, Dictionary<string, int>>(
                rawStats.Count,
                StringComparer.Ordinal
            );
            foreach (var entry in rawStats)
            {
                if (entry.Value is not { Count: > 0 })
                    continue;
                var perCard = new Dictionary<string, int>(
                    entry.Value.Count,
                    StringComparer.Ordinal
                );
                foreach (var stat in entry.Value)
                    perCard[stat.Key.ToString()] = stat.Value;
                if (perCard.Count > 0)
                    cardStats[entry.Key] = perCard;
            }
            if (cardStats.Count == 0)
                cardStats = null;
        }

        return new PveCombatSummary
        {
            EncounterId = opening.EncounterId,
            Day = opening.Day,
            Hour = opening.Hour,
            Winner = combat.Winner.ToString(),
            Loser = combat.Loser.ToString(),
            FrameCount = frameCount,
            OpponentHealthThresholdsForGold = combat.OpponentHealthThresholdsForGold
                is { Count: > 0 } goldThresholds
                ? new List<float>(goldThresholds)
                : null,
            OpponentHealthThresholdsForXp = combat.OpponentHealthThresholdsForXp
                is { Count: > 0 } xpThresholds
                ? new List<float>(xpThresholds)
                : null,
            GoldThresholdsHit = goldHits,
            XpThresholdsHit = xpHits,
            CardStats = cardStats,
        };
    }
}
