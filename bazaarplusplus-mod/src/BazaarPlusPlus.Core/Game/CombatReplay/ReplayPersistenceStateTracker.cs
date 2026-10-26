#nullable enable
using System.Collections.Concurrent;

namespace BazaarPlusPlus.Game.CombatReplay;

public static class ReplayPersistenceStateTracker
{
    private static readonly ConcurrentDictionary<string, int> PendingByRun = new(
        StringComparer.Ordinal
    );

    // Bumped by every completion, whatever its run: the seal coordinator stamps a replay wait with
    // it, so a Replay Payload that lands afterwards forces a recompose (BundleSealCoordinator).
    // A writer must call Completed only after the payload file is written; the Android host
    // writes synchronously and never calls it (#513 plans an async queue that must keep that order).
    private static long _completionGeneration;

    public static long CompletionGeneration => Interlocked.Read(ref _completionGeneration);

    public static void Enqueued(string? runId)
    {
        if (!string.IsNullOrWhiteSpace(runId))
            PendingByRun.AddOrUpdate(runId, 1, (_, count) => count + 1);
    }

    public static void Completed(string? runId)
    {
        Interlocked.Increment(ref _completionGeneration);
        if (string.IsNullOrWhiteSpace(runId))
            return;
        while (PendingByRun.TryGetValue(runId, out var count))
        {
            if (count <= 1)
            {
                if (PendingByRun.TryRemove(runId, out _))
                    return;
            }
            else if (PendingByRun.TryUpdate(runId, count - 1, count))
                return;
        }
    }

    public static bool HasPending(string runId) =>
        !string.IsNullOrWhiteSpace(runId) && PendingByRun.ContainsKey(runId);
}
