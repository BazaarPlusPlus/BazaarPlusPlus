#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles.Persistence;

internal static partial class ReplayPayloadMaintenanceServiceTests
{
    private static void ParallelDeletionPreservesCatalogThreadAndOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var inventory = Enumerable
            .Range(0, 12)
            .Select(index => new ReplayPayloadMaintenanceRecord(
                $"pending-{index:D2}",
                now,
                ReplayPayloadState.DeletePending
            ))
            .ToList();
        var catalog = new RecordingMaintenanceCatalog(inventory);
        using var files = new PayloadDirectory(
            inventory
                .Select(row => row.BattleId)
                .Concat(Enumerable.Range(0, 4).Select(index => $"orphan-{index:D2}"))
        );
        using var gate = new ReplayPayloadOperationGate();
        var callerThread = Environment.CurrentManagedThreadId;
        var active = 0;
        var exceededBound = 0;
        var injectFailure = true;
        var service = new ReplayPayloadMaintenanceService(
            catalog,
            files.Store,
            gate,
            () => Array.Empty<string>(),
            id =>
            {
                if (Interlocked.Increment(ref active) > 4)
                    Interlocked.Exchange(ref exceededBound, 1);
                try
                {
                    if (injectFailure && id is "pending-03" or "orphan-02")
                        throw new IOException("injected file deletion failure");
                    files.Store.Delete(id);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }
        );

        var first = service.Run(now, CancellationToken.None);
        Equal(11, first.DeletedPayloadCount, "successful pending deletions");
        Equal(3, first.OrphanDeleteCount, "successful orphan deletions");
        Equal(2, first.FailedDeleteCount, "independent file failures");
        Equal(0, exceededBound, "file delete concurrency limit");
        Equal(0, active, "all deletes joined");
        Assert(
            catalog.CallThreads.All(thread => thread == callerThread),
            "Catalog operations must stay on the calling thread."
        );
        Assert(
            catalog
                .CompletedBatches.Single()
                .SequenceEqual(
                    inventory.Where(row => row.BattleId != "pending-03").Select(row => row.BattleId)
                ),
            "Completion IDs preserve the pending inventory order and exclude orphans."
        );
        Equal(
            ReplayPayloadState.DeletePending,
            catalog.Inventory.Single(row => row.BattleId == "pending-03").PayloadState,
            "failed pending row remains retryable"
        );
        injectFailure = false;
        var retry = service.Run(now, CancellationToken.None);
        Equal(1, retry.DeletedPayloadCount, "pending retry");
        Equal(1, retry.OrphanDeleteCount, "orphan retry");
        Equal(0, retry.FailedDeleteCount, "retry failure count");
        Equal(0, files.Count(), "retry removed the remaining files");
    }

    private static void CancellationKeepsTheLeaseUntilFileDeletionEnds()
    {
        var now = DateTimeOffset.UtcNow;
        var inventory = Enumerable
            .Range(0, 8)
            .Select(index => new ReplayPayloadMaintenanceRecord(
                $"cancel-{index:D2}",
                now,
                ReplayPayloadState.DeletePending
            ))
            .ToList();
        var catalog = new RecordingMaintenanceCatalog(inventory);
        using var files = new PayloadDirectory(inventory.Select(row => row.BattleId));
        using var gate = new ReplayPayloadOperationGate();
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var runThread = 0;
        var active = 0;
        var service = new ReplayPayloadMaintenanceService(
            catalog,
            files.Store,
            gate,
            () => Array.Empty<string>(),
            id =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    files.Store.Delete(id);
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Test did not release file deletion.");
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }
        );
        var run = Task.Run(() =>
        {
            runThread = Environment.CurrentManagedThreadId;
            return service.Run(now, cancellation.Token);
        });
        try
        {
            Assert(entered.Wait(TimeSpan.FromSeconds(10)), "File deletion did not begin.");
            cancellation.Cancel();
            using var playback = gate.TryAcquirePlayback();
            Assert(
                playback == null && !run.IsCompleted,
                "Cancellation must retain the maintenance lease while a deletion is blocked."
            );
        }
        finally
        {
            release.Set();
        }
        try
        {
            run.GetAwaiter().GetResult();
            throw new InvalidOperationException("Canceled deletion unexpectedly completed.");
        }
        catch (OperationCanceledException) { }
        Equal(0, active, "cancellation joins started deletes");
        Equal(0, catalog.CompleteCallCount, "cancellation leaves pending records for recovery");
        Assert(
            catalog.CallThreads.All(thread => thread == runThread),
            "Canceled catalog calls stay on the maintenance thread."
        );
        using (var playback = gate.TryAcquirePlayback())
            Assert(playback != null, "Playback can acquire the lease after canceled deletes stop.");

        var retry = service.Run(now, CancellationToken.None);
        Equal(
            8,
            retry.DeletedPayloadCount,
            "next pass completes deleted and still present pending files"
        );
        Equal(0, files.Count(), "recovery removed remaining files");
        Assert(
            catalog.Inventory.All(row => row.PayloadState == ReplayPayloadState.Evicted),
            "Recovery completes every pending row."
        );
    }
}
