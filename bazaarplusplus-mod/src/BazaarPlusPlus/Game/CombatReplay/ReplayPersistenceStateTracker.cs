#nullable enable
using System.Collections.Concurrent;

namespace BazaarPlusPlus.Game.CombatReplay;

internal static class ReplayPersistenceStateTracker
{
    private static readonly ConcurrentDictionary<string, int> PendingByRun = new(
        StringComparer.Ordinal
    );

    internal static void Enqueued(string? runId)
    {
        if (!string.IsNullOrWhiteSpace(runId))
            PendingByRun.AddOrUpdate(runId, 1, (_, count) => count + 1);
    }

    internal static void Completed(string? runId)
    {
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

    internal static bool HasPending(string runId) =>
        !string.IsNullOrWhiteSpace(runId) && PendingByRun.ContainsKey(runId);
}
