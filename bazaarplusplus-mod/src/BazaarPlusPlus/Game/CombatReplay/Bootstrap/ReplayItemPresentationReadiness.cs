#nullable enable
using System.Collections;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplayItemPresentationReadiness
{
    private static readonly ReplayPresentationTaskTracker Tracker = new();

    internal static IDisposable BeginTracking() => Tracker.BeginTracking();

    internal static Task Track(Task task) => Tracker.Track(task);

    internal static async Task WaitForTrackedTasksAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var pending = Tracker.SnapshotPending();
            if (pending.Length > 0)
            {
                await AwaitBeforeDeadlineAsync(Task.WhenAll(pending), deadline);
                continue;
            }

            await AwaitBeforeDeadlineAsync(WaitForRenderBoundaryAsync(), deadline);
            if (Tracker.SnapshotPending().Length == 0)
                return;
        }
    }

    private static async Task AwaitBeforeDeadlineAsync(Task task, DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero || await Task.WhenAny(task, Task.Delay(remaining)) != task)
            throw new TimeoutException("Timed out while waiting for replay item presentation.");

        await task;
    }

    private static Task WaitForRenderBoundaryAsync()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
            throw new InvalidOperationException("BoardManager is unavailable.");

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        boardManager.StartCoroutine(CompleteAfterRenderBoundary(completion));
        return completion.Task;
    }

    private static IEnumerator CompleteAfterRenderBoundary(TaskCompletionSource<bool> completion)
    {
        yield return new WaitForEndOfFrame();
        completion.TrySetResult(true);
    }
}
