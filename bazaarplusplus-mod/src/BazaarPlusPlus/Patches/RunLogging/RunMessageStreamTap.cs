#nullable enable
#pragma warning disable CS0436
using System.Runtime.CompilerServices;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Events;
using MessagePack;

namespace BazaarPlusPlus.Patches.RunLogging;

// Serialize on the main thread before dispatch because game handlers mutate some messages.
internal static class RunMessageStreamTap
{
    private static readonly ConditionalWeakTable<INetMessage, object> Recorded = new();
    private static readonly ConditionalWeakTable<INetMessage, object> ResponseCommands = new();
    private static readonly object Seen = new();
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
