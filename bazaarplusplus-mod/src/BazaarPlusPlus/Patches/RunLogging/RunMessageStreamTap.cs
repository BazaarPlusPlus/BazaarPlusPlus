#nullable enable
#pragma warning disable CS0436
using System.Runtime.CompilerServices;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using MessagePack;

namespace BazaarPlusPlus.Patches.RunLogging;

/// <summary>
/// Turns what the desktop client receives and sends into Run Message Stream frames (ADR-0028).
/// A message is serialized when it reaches the dispatch seam, before the game handles it: the game
/// changes some messages while handling them. Main thread.
/// </summary>
internal static class RunMessageStreamTap
{
    private static readonly ConditionalWeakTable<INetMessage, object> Recorded = new();
    private static readonly ConditionalWeakTable<INetMessage, object> ResponseCommands = new();
    private static readonly object Seen = new();
    private static int _lastCommandNumber;

    /// <summary>
    /// At the dispatch seam. An aggregate's messages are recorded with it, so when they pass the
    /// seam on their own they are skipped.
    /// </summary>
    internal static void OnReceived(INetMessage message)
    {
        if (Recorded.TryGetValue(message, out _))
            return;
        int? respondsTo = ResponseCommands.TryGetValue(message, out var number)
            ? (int)number
            : null;
        Record(message, respondsTo);
    }

    /// <summary>A command method was called. Returns its number.</summary>
    internal static int OnCommand(string method, string argsJson)
    {
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

    /// <summary>
    /// The server answered command <paramref name="number"/>; the game has not handled the
    /// response yet. A response that never reaches the seam is recorded here.
    /// </summary>
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
        if (
            message
            is not (NetMessageGameSim or NetMessageGameStateSync or NetMessageRunInitialized)
        )
            return;
        BppPatchHost.Services.EventBus.Publish(
            new RunMessageStreamFrameObserved
            {
                Message = MessagePackSerializer.Serialize(message, MessagePackConfig.Options),
                RespondsToCommand = respondsTo,
            }
        );
    }
}
