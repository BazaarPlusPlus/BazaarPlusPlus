using System.Diagnostics;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Domain.Core;
using Il2CppBazaarGameShared.Infra.Commands;
using Il2CppBazaarGameShared.Infra.Messages;
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
    public readonly record struct SentCommand(int Number, bool ReachesSeam);

    private enum MessageKind
    {
        Skipped,
        Kept,
        Aggregate,
    }

    private sealed record PendingResponse(int Number, GameResponse Task, bool ReachesSeam);

    // The game sends one command at a time; more than this many means responses that never came.
    private const int MaxPendingResponses = 8;

    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, MessageKind> Kinds = new();

    // An aggregate's messages, already recorded, while the game hands them to the seam one by one.
    private static readonly HashSet<IntPtr> AggregateChildren = new();
    private static readonly List<PendingResponse> Pending = new();
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
                Pending.RemoveAt(0);
            Pending.Add(new PendingResponse(sent.Number, new GameResponse(task), sent.ReachesSeam));
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
        if (kind != MessageKind.Kept)
            return;
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

    private static MessageKind KindOf(IntPtr message)
    {
        var type = IL2CPP.il2cpp_object_get_class(message);
        if (Kinds.TryGetValue(type, out var kind))
            return kind;
        kind =
            Is<NetMessageAggregate>(type) ? MessageKind.Aggregate
            : Is<NetMessageGameSim>(type)
            || Is<NetMessageGameStateSync>(type)
            || Is<NetMessageRunInitialized>(type)
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
