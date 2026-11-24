using System.Diagnostics;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Domain.Core;
using Il2CppBazaarGameShared.Infra.Commands;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppBazaarGameShared.Infra.Messages.CombatSimEvents;
using Il2CppInterop.Runtime;
using Newtonsoft.Json;
using GameResponse = Il2CppSystem.Threading.Tasks.Task<Il2CppBazaarGameShared.Core.Result<Il2CppBazaarGameShared.Infra.Messages.INetMessage>>;

namespace BazaarPlusPlus.Android.GameInterop;

// The Run Message Stream on this host (ADR-0028), in the desktop tap's frames and order. A message
// is serialized before the game handles it, with the game's serializer as an INetMessage. A
// command pairs with its response by the response object's address, read from the task the game
// awaits: a pending task keeps its response alive, so the address is not reused meanwhile.
internal static class AndroidRunMessageStream
{
    private const string MonsterCombatState = "Combat";

    public readonly record struct SentCommand(int Number, bool ReachesSeam);

    private sealed record PendingPveOpening(string? EncounterId, int? Day, int? Hour);

    private enum MessageKind
    {
        Skipped,
        Kept,
        Aggregate,
        CombatSim,
    }

    private sealed record PendingResponse(int Number, GameResponse Task, bool ReachesSeam);

    // The game sends one command at a time; more than this many means responses that never came.
    private const int MaxPendingResponses = 8;

    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, MessageKind> Kinds = new();

    // An aggregate's messages, already recorded, while the game hands them to the seam one by one.
    private static readonly HashSet<IntPtr> AggregateChildren = new();
    private static readonly List<PendingResponse> Pending = new();
    private static PendingPveOpening? _pendingPveOpening;
    private static int _unsettledDirectResponses;
    private static int _lastCommandNumber;
    private static long _messages;
    private static long _commands;
    private static long _paired;
    private static long _bytes;
    private static long _serializeTicks;
    private static long _serializeMaxTicks;

    public static BppLogField[] Summary() =>
        [
            ("stream_messages", Interlocked.Read(ref _messages)),
            ("stream_commands", Interlocked.Read(ref _commands)),
            ("stream_paired", Interlocked.Read(ref _paired)),
            ("stream_bytes", Interlocked.Read(ref _bytes)),
            ("stream_serialize_ms", Milliseconds(Interlocked.Read(ref _serializeTicks))),
            ("stream_serialize_max_ms", Milliseconds(Interlocked.Read(ref _serializeMaxTicks))),
        ];

    // Settle completed responses that bypass the dispatch seam (ReestablishSession) without
    // waiting for another command or blocking on any IL2CPP task.
    public static void Tick(IBppEventBus bus)
    {
        if (Volatile.Read(ref _unsettledDirectResponses) == 0)
            return;
        lock (Gate)
            Settle(bus, IntPtr.Zero);
    }

    public static void OnReceived(IBppEventBus bus, INetMessage message)
    {
        lock (Gate)
        {
            var pointer = message.Pointer;
            if (AggregateChildren.Remove(pointer))
                return;
            // What is left belongs to an aggregate the game stopped handling; its addresses can be
            // reused by later messages.
            AggregateChildren.Clear();
            Record(bus, message, Settle(bus, pointer), throughSeam: true);
        }
    }

    // Null for a command the stream does not hold.
    public static SentCommand? OnCommand(IBppEventBus bus, IntPtr command, bool sessionRecovery)
    {
        if (Describe(command, sessionRecovery) is not var (shape, values))
            return null;
        var arguments = new Dictionary<string, object?>();
        for (var index = 0; index < shape.Parameters.Length; index++)
            arguments[shape.Parameters[index]] = values[index];
        lock (Gate)
        {
            // A reconnect's response is recorded before the command the game retries after it.
            Settle(bus, IntPtr.Zero);
            if (shape == AndroidRunCommands.InitializeRun)
                _pendingPveOpening = null;
            var number = ++_lastCommandNumber;
            bus.Publish(
                new RunMessageStreamFrameObserved
                {
                    CommandNumber = number,
                    Command = shape.Method,
                    CommandArgsJson = JsonConvert.SerializeObject(arguments),
                    StartsRun = shape == AndroidRunCommands.InitializeRun,
                }
            );
            _commands++;
            // ReestablishSession's response never passes the dispatch seam.
            return new SentCommand(number, shape != AndroidRunCommands.ReestablishSession);
        }
    }

    public static void OnCommandSent(SentCommand sent, IntPtr task)
    {
        if (task == IntPtr.Zero)
            return;
        lock (Gate)
        {
            if (Pending.Count >= MaxPendingResponses)
            {
                if (!Pending[0].ReachesSeam)
                    DecrementUnsettledDirectResponses();
                Pending.RemoveAt(0);
            }
            Pending.Add(new PendingResponse(sent.Number, new GameResponse(task), sent.ReachesSeam));
            if (!sent.ReachesSeam)
                Volatile.Write(
                    ref _unsettledDirectResponses,
                    Volatile.Read(ref _unsettledDirectResponses) + 1
                );
        }
    }

    // The command whose response is the message at `received`, after recording every finished
    // response that does not pass the seam.
    private static int? Settle(IBppEventBus bus, IntPtr received)
    {
        int? respondsTo = null;
        for (var index = 0; index < Pending.Count; )
        {
            var pending = Pending[index];
            if (!pending.Task.IsCompleted)
            {
                index++;
                continue;
            }
            var response = Response(pending.Task);
            if (response != null && pending.ReachesSeam && response.Pointer != received)
            {
                index++;
                continue;
            }
            Pending.RemoveAt(index);
            if (!pending.ReachesSeam)
                DecrementUnsettledDirectResponses();
            if (response == null)
                continue;
            _paired++;
            if (pending.ReachesSeam)
                respondsTo = pending.Number;
            else
                Record(bus, response, pending.Number, throughSeam: false);
        }
        return respondsTo;
    }

    private static void DecrementUnsettledDirectResponses()
    {
        var current = Volatile.Read(ref _unsettledDirectResponses);
        if (current > 0)
            Volatile.Write(ref _unsettledDirectResponses, current - 1);
    }

    private static INetMessage? Response(GameResponse task)
    {
        if (task.IsFaulted || task.IsCanceled)
            return null;
        var result = task.Result;
        return result is { IsOk: true } ? result.Value : null;
    }

    private static void Record(
        IBppEventBus bus,
        INetMessage message,
        int? respondsTo,
        bool throughSeam
    )
    {
        var kind = KindOf(message.Pointer);
        if (kind == MessageKind.Aggregate)
        {
            var children = message.Cast<NetMessageAggregate>().Messages;
            for (var index = 0; children != null && index < children.Count; index++)
            {
                var child = children[index];
                if (child == null)
                    continue;
                if (throughSeam)
                    AggregateChildren.Add(child.Pointer);
                Record(bus, child, respondsTo, throughSeam);
            }
            return;
        }
        if (kind == MessageKind.CombatSim)
        {
            var opening = _pendingPveOpening;
            _pendingPveOpening = null;
            if (opening != null)
            {
                var combatSim = message.TryCast<NetMessageCombatSim>();
                if (combatSim?.Data is { } combat)
                {
                    bus.Publish(
                        new RunMessageStreamFrameObserved
                        {
                            PveCombat = ExtractPveSummary(opening, combat),
                        }
                    );
                }
            }
            return;
        }
        if (kind != MessageKind.Kept)
            return;
        UpdatePendingPveOpening(message);
        var started = Stopwatch.GetTimestamp();
        var bytes = AndroidReplayMessages.Serialize(message);
        var ticks = Stopwatch.GetTimestamp() - started;
        _messages++;
        _bytes += bytes.Length;
        _serializeTicks += ticks;
        _serializeMaxTicks = Math.Max(_serializeMaxTicks, ticks);
        bus.Publish(
            new RunMessageStreamFrameObserved { Message = bytes, RespondsToCommand = respondsTo }
        );
    }

    private static void UpdatePendingPveOpening(INetMessage message)
    {
        var type = IL2CPP.il2cpp_object_get_class(message.Pointer);
        if (Is<NetMessageGameSim>(type))
        {
            var data = message.Cast<NetMessageGameSim>().Data;
            var stateName = data?.CurrentState?.StateName.ToString();
            _pendingPveOpening = string.Equals(
                stateName,
                MonsterCombatState,
                StringComparison.Ordinal
            )
                ? new PendingPveOpening(
                    data?.CurrentState?.CurrentEncounterId,
                    data?.Run is { } dayRun ? unchecked((int)dayRun.Day) : null,
                    data?.Run is { } hourRun ? unchecked((int)hourRun.Hour) : null
                )
                : null;
            return;
        }
        if (Is<NetMessageRunInitialized>(type) || Is<NetMessageGameStateSync>(type))
            _pendingPveOpening = null;
    }

    private static PveCombatSummary ExtractPveSummary(PendingPveOpening opening, CombatSim combat)
    {
        var goldHits = 0;
        var xpHits = 0;
        var goldEventClass =
            Il2CppClassPointerStore<CombatSimEventMonsterGoldReceived>.NativeClassPtr;
        var xpEventClass = Il2CppClassPointerStore<CombatSimEventMonsterXpReceived>.NativeClassPtr;
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
                    var ev = events[e];
                    if (ev == null)
                        continue;
                    var evClass = IL2CPP.il2cpp_object_get_class(ev.Pointer);
                    if (evClass == goldEventClass)
                        goldHits++;
                    else if (evClass == xpEventClass)
                        xpHits++;
                }
            }
        }

        List<float>? goldThresholds = null;
        if (combat.OpponentHealthThresholdsForGold is { Count: > 0 } rawGold)
        {
            goldThresholds = new List<float>(rawGold.Count);
            for (var index = 0; index < rawGold.Count; index++)
                goldThresholds.Add(rawGold[index]);
        }

        List<float>? xpThresholds = null;
        if (combat.OpponentHealthThresholdsForXp is { Count: > 0 } rawXp)
        {
            xpThresholds = new List<float>(rawXp.Count);
            for (var index = 0; index < rawXp.Count; index++)
                xpThresholds.Add(rawXp[index]);
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
                var key = entry.Key;
                var value = entry.Value;
                if (string.IsNullOrEmpty(key) || value is not { Count: > 0 })
                    continue;
                var perCard = new Dictionary<string, int>(value.Count, StringComparer.Ordinal);
                foreach (var stat in value)
                    perCard[stat.Key.ToString()] = stat.Value;
                if (perCard.Count > 0)
                    cardStats[key] = perCard;
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
            OpponentHealthThresholdsForGold = goldThresholds,
            OpponentHealthThresholdsForXp = xpThresholds,
            GoldThresholdsHit = goldHits,
            XpThresholdsHit = xpHits,
            CardStats = cardStats,
        };
    }

    private static MessageKind KindOf(IntPtr message)
    {
        var type = IL2CPP.il2cpp_object_get_class(message);
        if (Kinds.TryGetValue(type, out var kind))
            return kind;
        kind =
            Is<NetMessageAggregate>(type) ? MessageKind.Aggregate
            : Is<NetMessageCombatSim>(type) ? MessageKind.CombatSim
            : Is<NetMessageGameSim>(type)
            || Is<NetMessageGameStateSync>(type)
            || Is<NetMessageRunInitialized>(type)
            || Is<NetMessageError>(type)
                ? MessageKind.Kept
            : MessageKind.Skipped;
        Kinds[type] = kind;
        return kind;
    }

    private static bool Is<T>(IntPtr type)
        where T : Il2CppSystem.Object =>
        IL2CPP.il2cpp_class_is_assignable_from(Il2CppClassPointerStore<T>.NativeClassPtr, type);

    // The command classes are sealed, so the class decides the HttpGameClient method. Only
    // ReestablishSession sends its command with session recovery off.
    private static (AndroidRunCommands.Shape Shape, object?[] Values)? Describe(
        IntPtr command,
        bool sessionRecovery
    )
    {
        if (command == IntPtr.Zero)
            return null;
        var type = IL2CPP.il2cpp_object_get_class(command);
        if (type == Il2CppClassPointerStore<InitializeRunCommand>.NativeClassPtr)
        {
            if (!sessionRecovery)
                return (AndroidRunCommands.ReestablishSession, []);
            var initialize = new InitializeRunCommand(command);
            return (
                AndroidRunCommands.InitializeRun,
                [
                    initialize.PlayMode.ToString(),
                    initialize.SelectedHero.ToString(),
                    AndroidGameIds.Unboxed(() => initialize.GameModeId)?.ToString(),
                ]
            );
        }
        if (type == Il2CppClassPointerStore<SelectItemCommand>.NativeClassPtr)
        {
            var select = new SelectItemCommand(command);
            return (
                AndroidRunCommands.SelectItem,
                [
                    Id(select.InstanceId),
                    Sockets(select.TargetSockets),
                    AndroidGameIds.Unboxed(() => select.Section)?.ToString(),
                ]
            );
        }
        if (type == Il2CppClassPointerStore<MoveItemCommand>.NativeClassPtr)
        {
            var move = new MoveItemCommand(command);
            return (
                AndroidRunCommands.MoveItem,
                [Id(move.InstanceId), Sockets(move.TargetSockets), move.Section.ToString()]
            );
        }
        if (type == Il2CppClassPointerStore<SelectSkillCommand>.NativeClassPtr)
            return (
                AndroidRunCommands.SelectSkill,
                [Id(new SelectSkillCommand(command).InstanceId)]
            );
        if (type == Il2CppClassPointerStore<SelectEncounterCommand>.NativeClassPtr)
            return (
                AndroidRunCommands.SelectEncounter,
                [Id(new SelectEncounterCommand(command).InstanceId)]
            );
        if (type == Il2CppClassPointerStore<SellCardCommand>.NativeClassPtr)
            return (AndroidRunCommands.SellCard, [Id(new SellCardCommand(command).InstanceId)]);
        if (type == Il2CppClassPointerStore<CommitToPedestalCommand>.NativeClassPtr)
            return (
                AndroidRunCommands.CommitToPedestal,
                [Id(new CommitToPedestalCommand(command).InstanceId)]
            );
        if (type == Il2CppClassPointerStore<RerollCommand>.NativeClassPtr)
            return (AndroidRunCommands.ReRollSelection, []);
        if (type == Il2CppClassPointerStore<ExitCurrentStateCommand>.NativeClassPtr)
            return (AndroidRunCommands.ExitCurrentState, []);
        if (type == Il2CppClassPointerStore<AbandonRunCommand>.NativeClassPtr)
            return (AndroidRunCommands.AbandonRun, []);
        return null;
    }

    private static string? Id(InstanceId? id) => id?.Value;

    private static List<string>? Sockets<T>(Il2CppSystem.Collections.Generic.List<T>? sockets)
        where T : unmanaged, Enum
    {
        if (sockets == null)
            return null;
        var names = new List<string>(sockets.Count);
        for (var index = 0; index < sockets.Count; index++)
            names.Add(sockets[index].ToString());
        return names;
    }

    private static double Milliseconds(long ticks) =>
        Math.Round(ticks * 1000.0 / Stopwatch.Frequency, 1);
}
