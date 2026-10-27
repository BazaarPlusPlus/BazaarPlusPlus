#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class ReplayVideoMetadataLifecycleTests
{
    internal static void Run()
    {
        AttachmentAndFileStateRemainOrthogonal();
        ReconcileDoesNotOverwriteAConcurrentFinish();
        MainThreadReservationGivesUpOnALockedDatabase(enforceTiming: false);
    }

    // The performance lane (--benchmark) adds the wall-clock bound, which the parallel
    // functional run cannot hold reliably.
    internal static void Benchmark() =>
        MainThreadReservationGivesUpOnALockedDatabase(enforceTiming: true);

    // The current-replay arm reserves its row on the Unity main thread: while another writer
    // holds the database it fails with SQLITE_BUSY after about a second (the default store wait
    // is two), reserves nothing, and succeeds once the writer is gone.
    private static void MainThreadReservationGivesUpOnALockedDatabase(bool enforceTiming)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-reserve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            var store = new CombatReplayVideoMetadataStore(databasePath);
            using (var writer = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                writer.Open();
                Execute(writer, "BEGIN IMMEDIATE;");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                SqliteException? busy = null;
                try
                {
                    store.ReserveStartOnMainThread(
                        Started("locked", "locked-battle", "2026/locked.mp4")
                    );
                }
                catch (SqliteException ex)
                {
                    busy = ex;
                }
                stopwatch.Stop();

                Equal(5, busy?.SqliteErrorCode ?? 0, "reservation error code (SQLITE_BUSY)");
                if (enforceTiming && stopwatch.Elapsed >= TimeSpan.FromMilliseconds(1900))
                    throw new InvalidOperationException(
                        $"Expected the main-thread reservation to give up before the default "
                            + $"two-second wait, took {stopwatch.ElapsedMilliseconds} ms."
                    );

                Execute(writer, "ROLLBACK;");
            }

            Equal(0, store.ListArtifacts().Count, "rows reserved while locked");
            store.ReserveStartOnMainThread(Started("locked", "locked-battle", "2026/locked.mp4"));
            Equal("RECORDING", store.ListArtifacts().Single().Status, "reservation after unlock");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void ReconcileDoesNotOverwriteAConcurrentFinish()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-cas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            var store = new CombatReplayVideoMetadataStore(databasePath);
            store.SaveStart(Started("race", "race-battle", "2026/race.mp4"));
            var staleObservation = store.ListArtifacts().Single();

            store.SaveFinish(Finished("race", "2026/race.mp4", "COMPLETED", 300));
            store.ReconcileFileState(
                [staleObservation],
                ReplayVideoFileState.Missing,
                DateTimeOffset.UtcNow
            );

            var finished = store.ListArtifacts().Single();
            Equal("COMPLETED", finished.Status, "concurrent finish status");
            Equal(
                ReplayVideoFileState.Present,
                finished.FileState,
                "stale reconcile cannot overwrite concurrent finish"
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AttachmentAndFileStateRemainOrthogonal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                RunLogSchema.EnsureInitialized(connection);
                Execute(
                    connection,
                    """
                    INSERT INTO battles (
                        battle_id, source, recorded_at_ms, combat_kind,
                        local_payload_state
                    ) VALUES (
                        'attached-battle', 'LOCAL', 1785542400000, 'PVPCombat',
                        'missing'
                    );
                    """
                );
            }

            var store = new CombatReplayVideoMetadataStore(databasePath);
            store.SaveStart(Started("attached", "attached-battle", "2026/attached.mp4"));
            store.SaveFinish(Finished("attached", "2026/attached.mp4", "COMPLETED", 100));
            store.SaveStart(Started("detached", "deleted-battle", "2026/detached.mp4"));
            store.SaveFinish(Finished("detached", "2026/detached.mp4", "COMPLETED", 200));

            var artifacts = store.ListArtifacts();
            Equal(
                ReplayVideoAttachmentState.Attached,
                artifacts.Single(artifact => artifact.VideoId == "attached").AttachmentState,
                "attached metadata state"
            );
            Equal(
                ReplayVideoFileState.Present,
                artifacts.Single(artifact => artifact.VideoId == "attached").FileState,
                "completed file state"
            );
            Equal(
                ReplayVideoAttachmentState.Detached,
                artifacts.Single(artifact => artifact.VideoId == "detached").AttachmentState,
                "orphan recording attachment state"
            );

            var reconciledAt = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
            store.ReconcileFileState(
                [store.ListArtifacts().Single(artifact => artifact.VideoId == "detached")],
                ReplayVideoFileState.Missing,
                reconciledAt
            );
            var detachedMissing = store
                .ListArtifacts()
                .Single(artifact => artifact.VideoId == "detached");
            Equal(
                ReplayVideoAttachmentState.Detached,
                detachedMissing.AttachmentState,
                "missing reconcile preserves detach"
            );
            Equal(
                ReplayVideoFileState.Missing,
                detachedMissing.FileState,
                "missing reconcile state"
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static VideoRecordingStarted Started(string videoId, string battleId, string path) =>
        new()
        {
            VideoId = videoId,
            BattleId = battleId,
            Source = "LocalSaved",
            VideoRelativePath = path,
            Width = 1920,
            Height = 1080,
            Fps = 30,
            StartedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        };

    private static VideoRecordingFinished Finished(
        string videoId,
        string path,
        string status,
        long fileSize
    ) =>
        new()
        {
            VideoId = videoId,
            VideoRelativePath = path,
            EndedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 1, 0, TimeSpan.Zero),
            DurationMs = 60_000,
            CapturedFrames = 1_800,
            FileSizeBytes = fileSize,
            Status = status,
        };

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }
}
