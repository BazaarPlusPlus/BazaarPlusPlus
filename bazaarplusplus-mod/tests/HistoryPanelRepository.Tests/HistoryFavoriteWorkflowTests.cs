#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;

// Exercise the production workflow, SQLite and compressed files together. Only operations
// requiring the game process are supplied by the fixture; none of the persistence is mocked.
internal static class HistoryFavoriteWorkflowTests
{
    internal static async Task RunAsync(string root)
    {
        await CopyOutlivesRunAndRemovalIsIsolated(root);
        await UnavailableAndBusyLocalReplay(root);
        await CopyWriteFailureKeepsFavoriteWithoutReplay(root);
        await InvalidCopyIsRemoved(root);
        await FailedInsertRemovesVerifiedCopyAndCanRetry(root);
        await MissingSourceRemovesVerifiedCopy(root);
        await RepeatedSavePreservesFirstFavorite(root);
        await OverlappingSavesPreserveFirstCopy(root);
        await SweepWhileLoadingKeepsFavoritesAndRecentOrphans(root);
        await SweepWaitsForPendingFavoriteInsert(root);
        await ReplayRevalidatesCopyAndSelection(root);
        Console.WriteLine("History favorite workflow tests passed.");
    }

    private static async Task CopyOutlivesRunAndRemovalIsIsolated(string root)
    {
        var f = new Fixture(root);
        var callerThread = Environment.CurrentManagedThreadId;
        var save = f.AddLocal();
        Check(
            f.LocalLoadThread == callerThread,
            "Local replay loading must stay on the caller's thread."
        );
        Check((await save).Outcome == FavoriteAddOutcome.Added, "Save a replayable favorite.");
        Check(
            f.Service.UpdateNote(Fixture.LocalId, "\r\n  My build  \r\n"),
            "Edit through the workflow."
        );
        f.Repository.DeleteRun(Fixture.RunId);
        f.Originals.Delete(Fixture.LocalId);
        var favorite = f.Favorite(Fixture.LocalId);
        Check(
            favorite.Note == "My build" && favorite.Battle.OpponentName == "Rival",
            "Deleting the Run must preserve the favorite's note and display facts."
        );
        Check(
            f.Service.LoadSnapshots(Fixture.LocalId)?.PlayerHand.Items.Single().TemplateId
                == "local-card",
            "Deleting the Run must preserve the copied boards."
        );
        Check(
            (await f.Service.ReplayAsync(favorite, true, CancellationToken.None)).Succeeded,
            "Replay must still start after both the original Run and payload are gone."
        );
        Check(
            f.LastReplay is { Source: CombatReplayPlaybackSource.Favorite, RecordVideo: true }
                && f.LastReplay.Payload.CombatMessageBytes.SequenceEqual(new byte[] { 2, 3 }),
            "Playback must receive the independent copy and Favorite source, including the recording intent."
        );
        Check(f.LocalLoads == 1, "Playing a favorite must not reload its original battle.");

        Check(
            (await f.AddGhost()).Outcome == FavoriteAddOutcome.Added,
            "Save an unrelated Ghost favorite."
        );
        Check(
            await f.Service.RemoveAsync(Fixture.LocalId, CancellationToken.None),
            "Remove the favorite."
        );
        Check(
            !f.Service.IsFavorite(Fixture.LocalId) && !File.Exists(f.CopyPath(Fixture.LocalId)),
            "Removal must delete both the row and its copy."
        );
        Check(
            f.Service.IsFavorite(Fixture.GhostId) && File.Exists(f.CopyPath(Fixture.GhostId)),
            "Removal must leave another favorite and its copy alone."
        );
        Check(
            !await f.Service.RemoveAsync(Fixture.LocalId, CancellationToken.None),
            "Removing twice must report nothing removed."
        );
    }

    private static async Task UnavailableAndBusyLocalReplay(string root)
    {
        var f = new Fixture(root);
        f.LocalOutcome = SavedBattleLoadOutcome.Busy;
        Check(
            (await f.AddLocal()).Outcome == FavoriteAddOutcome.Busy
                && !f.Service.IsFavorite(Fixture.LocalId)
                && !File.Exists(f.CopyPath(Fixture.LocalId)),
            "A busy payload lease must be retryable without storing an unavailable favorite."
        );
        f.LocalOutcome = SavedBattleLoadOutcome.Unavailable;
        Check(
            (await f.AddLocal()).Outcome == FavoriteAddOutcome.AddedWithoutReplay,
            "An unavailable runtime must still allow a favorite without a replay."
        );
        Check(
            !f.Service.CanReplay(f.Favorite(Fixture.LocalId), out _),
            "An unavailable copy cannot replay."
        );
    }

    private static async Task CopyWriteFailureKeepsFavoriteWithoutReplay(string root)
    {
        var f = new Fixture(root);
        Directory.CreateDirectory(f.CopyPath(Fixture.LocalId));
        Check(
            (await f.AddLocal()).Outcome == FavoriteAddOutcome.AddedWithoutReplay,
            "An actual file promotion failure must retain the favorite without a replay."
        );
        Check(
            f.Favorite(Fixture.LocalId).Battle.Replay == ReplayAvailability.Unavailable
                && !File.Exists(f.CopyPath(Fixture.LocalId))
                && !Directory.EnumerateFiles(f.CopyDirectory, "*.tmp").Any(),
            "A failed copy must not advertise replay or leave temporary files."
        );
    }

    private static async Task InvalidCopyIsRemoved(string root)
    {
        foreach (var wrongIdentity in new[] { false, true })
        {
            var f = new Fixture(root);
            var original = f.Originals.LoadDetailed(Fixture.LocalId).Payload!;
            if (wrongIdentity)
                f.LocalManifest.BattleId = "different-battle";
            else
                original.CombatMessageBytes = [];
            f.Originals.Save(original);
            Check(
                (await f.AddLocal()).Outcome == FavoriteAddOutcome.AddedWithoutReplay,
                "A deserialized copy with a wrong identity or missing combat input must fail verification."
            );
            Check(
                f.Favorite(Fixture.LocalId).Battle.Replay == ReplayAvailability.Unavailable
                    && !File.Exists(f.CopyPath(Fixture.LocalId)),
                "Verification failure must remove the copy before storing the unavailable favorite."
            );
        }
    }

    private static async Task FailedInsertRemovesVerifiedCopyAndCanRetry(string root)
    {
        var f = new Fixture(root);
        f.Execute(
            """
            CREATE TRIGGER reject_favorite BEFORE INSERT ON favorite_battles
            BEGIN SELECT RAISE(ABORT, 'favorite insert rejected'); END;
            """
        );
        var result = await f.AddLocal();
        Check(
            result.Outcome == FavoriteAddOutcome.Failed
                && result.Error?.Contains("favorite insert rejected", StringComparison.Ordinal)
                    == true,
            "A real SQLite insert failure must be reported."
        );
        Check(
            !f.Service.IsFavorite(Fixture.LocalId) && !File.Exists(f.CopyPath(Fixture.LocalId)),
            "A rejected insert must roll back the verified copy."
        );
        f.Execute("DROP TRIGGER reject_favorite;");
        Check(
            (await f.AddLocal()).Outcome == FavoriteAddOutcome.Added,
            "A failed save must release the operation gate and permit retry."
        );
    }

    private static async Task MissingSourceRemovesVerifiedCopy(string root)
    {
        var f = new Fixture(root);
        f.AfterLocalLoad = () => f.Repository.DeleteRun(Fixture.RunId);
        Check(
            (await f.AddLocal()).Outcome == FavoriteAddOutcome.Failed,
            "If the Run disappears during loading, no favorite can be inserted."
        );
        Check(
            !f.Service.IsFavorite(Fixture.LocalId) && !File.Exists(f.CopyPath(Fixture.LocalId)),
            "A zero-row insert must remove the verified copy too."
        );
    }

    private static async Task RepeatedSavePreservesFirstFavorite(string root)
    {
        var f = new Fixture(root);
        await f.AddLocal();
        var first = f.Favorite(Fixture.LocalId);
        var bytes = TestInputs.ScratchBytes(f.CopyPath(Fixture.LocalId));
        f.Service.UpdateNote(Fixture.LocalId, "Keep me");
        f.LocalOutcome = SavedBattleLoadOutcome.Busy;
        Check(
            (await f.AddLocal()).Outcome == FavoriteAddOutcome.AlreadyFavorite && f.LocalLoads == 1,
            "A repeated save must avoid reloading the original, even when its lease is now busy."
        );
        Check(
            f.Favorite(Fixture.LocalId).FavoritedAtUtc == first.FavoritedAtUtc
                && f.Favorite(Fixture.LocalId).Note == "Keep me"
                && TestInputs.ScratchBytes(f.CopyPath(Fixture.LocalId)).SequenceEqual(bytes),
            "A repeated save must preserve the first timestamp, note and exact copy."
        );
    }

    private static async Task OverlappingSavesPreserveFirstCopy(string root)
    {
        var f = new Fixture(root);
        var firstLoad = new TaskCompletionSource<GhostReplayLoad>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var secondLoad = new TaskCompletionSource<GhostReplayLoad>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var loads = 0;
        f.LoadGhost = (_, account, _) =>
        {
            Check(account == Fixture.Account, "The owning account must reach the Ghost loader.");
            return ++loads == 1 ? firstLoad.Task : secondLoad.Task;
        };
        // Both pass the fast precheck before either finishes loading. This is a service-level
        // contract: the current UI serializes favorite actions, so it is not a proven UI bug.
        var first = f.AddGhost();
        var second = f.AddGhost();
        Check(loads == 2, "Both saves must have entered loading before the first commits.");
        firstLoad.SetResult(f.GhostReplay(7));
        Check((await first).Outcome == FavoriteAddOutcome.Added, "The first loaded copy wins.");
        var firstFavorite = f.Favorite(Fixture.GhostId);
        var bytes = TestInputs.ScratchBytes(f.CopyPath(Fixture.GhostId));
        f.Service.UpdateNote(Fixture.GhostId, "First copy");
        secondLoad.SetResult(f.GhostReplay(9));
        Check(
            (await second).Outcome == FavoriteAddOutcome.AlreadyFavorite,
            "The stale save must be idempotent."
        );
        Check(
            File.Exists(f.CopyPath(Fixture.GhostId))
                && TestInputs.ScratchBytes(f.CopyPath(Fixture.GhostId)).SequenceEqual(bytes),
            "An overlapping save must neither replace nor delete the first favorite's copy."
        );
        Check(
            f.Favorite(Fixture.GhostId).FavoritedAtUtc == firstFavorite.FavoritedAtUtc
                && f.Favorite(Fixture.GhostId).Note == "First copy"
                && f.Service.LoadSnapshots(Fixture.GhostId)?.PlayerHand.Items.Single().TemplateId
                    == "ghost-card-7",
            "The first favorite's metadata and recorder-perspective boards must survive the stale save."
        );
    }

    private static async Task SweepWhileLoadingKeepsFavoritesAndRecentOrphans(string root)
    {
        var f = new Fixture(root);
        await f.AddLocal();
        f.AgeCopy(Fixture.LocalId);
        f.Copies.Save(Fixture.Copy("old-orphan"));
        f.AgeCopy("old-orphan");
        f.Copies.Save(Fixture.Copy("recent-orphan"));
        var loading = new TaskCompletionSource<GhostReplayLoad>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        f.LoadGhost = (_, _, _) => loading.Task;
        var save = f.AddGhost();
        Check(
            await f.Service.SweepOrphanCopiesAsync(CancellationToken.None) == 1,
            "A sweep during source loading must remove only the old orphan."
        );
        Check(
            File.Exists(f.CopyPath(Fixture.LocalId)) && File.Exists(f.CopyPath("recent-orphan")),
            "The sweep must preserve referenced copies of any age and young unreferenced copies."
        );
        loading.SetResult(f.GhostReplay(8));
        Check(
            (await save).Outcome == FavoriteAddOutcome.Added,
            "Save after source loading finishes."
        );
        Check(
            (
                await f.Service.ReplayAsync(
                    f.Favorite(Fixture.GhostId),
                    false,
                    CancellationToken.None
                )
            ).Succeeded,
            "The copy saved across a sweep must remain usable."
        );

        // Model a failed file delete/crash leaving a copy behind after its row is removed.
        f.Repository.DeleteFavorite(Fixture.LocalId);
        Check(
            await f.Service.SweepOrphanCopiesAsync(CancellationToken.None) == 1
                && !File.Exists(f.CopyPath(Fixture.LocalId)),
            "A later sweep must collect a removed favorite's old copy."
        );
    }

    private static async Task SweepWaitsForPendingFavoriteInsert(string root)
    {
        var f = new Fixture(root);
        // Warm schema initialization before taking the SQLite writer lock: subsequent
        // membership reads can proceed while the favorite INSERT must wait for this writer.
        Check(!f.Service.IsFavorite(Fixture.LocalId), "Begin without a favorite row.");
        using var writer = f.OpenConnection();
        using var transaction = writer.BeginTransaction(deferred: false);
        var copyWritten = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var watcher = new FileSystemWatcher(
            f.CopyDirectory,
            Path.GetFileName(f.CopyPath(Fixture.LocalId))
        )
        {
            NotifyFilter = NotifyFilters.FileName,
        };
        watcher.Created += (_, _) => copyWritten.TrySetResult();
        watcher.Renamed += (_, _) => copyWritten.TrySetResult();
        watcher.EnableRaisingEvents = true;

        var save = f.AddLocal();
        Task<int>? sweep = null;
        var sweepCompletedBeforeInsert = false;
        var pendingCopySurvived = false;
        try
        {
            // AtomicFileWriter promotes the complete file. This event, rather than a delay,
            // puts the test inside the copy-written/row-not-inserted window.
            await copyWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(
                !save.IsCompleted && !f.Service.IsFavorite(Fixture.LocalId),
                "The SQLite writer must hold the favorite INSERT after the copy is written."
            );
            Check(
                f.Copies.LoadDetailed(Fixture.LocalId).Status == FileBackedPayloadLoadStatus.Loaded,
                "The pending copy must be a complete, readable file."
            );
            f.AgeCopy(Fixture.LocalId);
            sweep = f.Service.SweepOrphanCopiesAsync(CancellationToken.None);
            try
            {
                // Only a bounded non-completion assertion: the transaction holds the window
                // open regardless of scheduling. Stay below AddFavorite's two-second timeout.
                await sweep.WaitAsync(TimeSpan.FromMilliseconds(500));
                sweepCompletedBeforeInsert = true;
            }
            catch (TimeoutException) { }
            Check(
                !save.IsCompleted && !f.Service.IsFavorite(Fixture.LocalId),
                "The INSERT must still be blocked when the pending sweep is observed."
            );
            pendingCopySurvived = File.Exists(f.CopyPath(Fixture.LocalId));
        }
        finally
        {
            transaction.Rollback();
            // Settle both operations before the caller cleans up files, even on assertion failure.
            await save.WaitAsync(TimeSpan.FromSeconds(5));
            if (sweep != null)
                await sweep.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Check(
            !sweepCompletedBeforeInsert && pendingCopySurvived,
            "The sweep must wait for the favorite INSERT instead of deleting its pending copy."
        );
        Check(
            (await save).Outcome == FavoriteAddOutcome.Added && await sweep! == 0,
            "Releasing the SQLite writer must let save finish before the sweep reads its inventory."
        );
        Check(
            f.Service.IsFavorite(Fixture.LocalId) && File.Exists(f.CopyPath(Fixture.LocalId)),
            "The completed favorite must retain both its row and its old replay copy."
        );
        Check(
            (
                await f.Service.ReplayAsync(
                    f.Favorite(Fixture.LocalId),
                    false,
                    CancellationToken.None
                )
            ).Succeeded,
            "The copy protected during INSERT must remain replayable."
        );
    }

    private static async Task ReplayRevalidatesCopyAndSelection(string root)
    {
        var f = new Fixture(root);
        await f.AddLocal();
        var favorite = f.Favorite(Fixture.LocalId);
        try
        {
            await f.Service.ReplayAsync(favorite, false, CancellationToken.None, () => false);
            throw new InvalidOperationException(
                "An obsolete selection must cancel before runtime playback."
            );
        }
        catch (OperationCanceledException) { }
        Check(f.LastReplay == null, "Selection cancellation must not start playback.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try
            {
                await f.Service.ReplayAsync(favorite, false, cancellation.Token);
                throw new InvalidOperationException("A cancelled session must not start playback.");
            }
            catch (OperationCanceledException) { }
        }
        Check(f.LastReplay == null, "Session cancellation must not start playback.");
        File.WriteAllBytes(f.CopyPath(Fixture.LocalId), [0, 1, 2]);
        Check(
            (await f.Service.ReplayAsync(favorite, false, CancellationToken.None)).ReasonCode
                == HistoryPanelReplayReasonCode.ReplayPayloadInvalid,
            "Replay must revalidate stored bytes."
        );
        File.Delete(f.CopyPath(Fixture.LocalId));
        Check(
            (await f.Service.ReplayAsync(favorite, false, CancellationToken.None)).ReasonCode
                == HistoryPanelReplayReasonCode.ReplayPayloadMissing,
            "Replay must distinguish a missing copy."
        );
        Check(f.LastReplay == null, "An invalid or missing copy must not reach playback.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record Playback(
        PvpReplayPayload Payload,
        bool RecordVideo,
        CombatReplayPlaybackSource Source
    );

    private sealed class Fixture
    {
        internal const string RunId = "favorite-run";
        internal const string LocalId = "favorite-local";
        internal const string GhostId = "favorite-ghost";
        internal const string Account = "local-account";
        private readonly string _databasePath;
        private readonly HistoryBattleRecord _local;
        private readonly HistoryBattleRecord _ghost;
        internal readonly HistoryPanelRepository Repository;
        internal readonly HistoryFavoriteService Service;
        internal readonly CombatReplayPayloadStore Originals;
        internal readonly PvpBattleManifest LocalManifest = new() { BattleId = LocalId };
        internal readonly FavoriteBattlePayloadStore Copies;
        internal readonly string CopyDirectory;
        internal SavedBattleLoadOutcome LocalOutcome = SavedBattleLoadOutcome.Loaded;
        internal Action? AfterLocalLoad;
        internal int LocalLoads;
        internal int LocalLoadThread;
        internal Playback? LastReplay;
        internal Func<
            HistoryBattleRecord,
            string,
            CancellationToken,
            Task<GhostReplayLoad>
        > LoadGhost;

        internal Fixture(string root)
        {
            root = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            _databasePath = Path.Combine(root, "history.sqlite3");
            using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
            {
                connection.Open();
                RunLogSchema.EnsureInitialized(connection);
            }
            Execute(
                """
                INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode)
                VALUES ('favorite-run', 1767225600000, 1767225600000, 'completed', 'Vanessa', 'Ranked');
                INSERT INTO battles (battle_id, source, run_id, recorded_at_ms, combat_kind, local_payload_state, opponent_name)
                VALUES ('favorite-local', 'LOCAL', 'favorite-run', 1767225600000, 'PVPCombat', 'ready', 'Rival');
                INSERT INTO battle_snapshots (battle_id, player_hand_json, player_skills_json,
                    opponent_hand_json, opponent_skills_json)
                VALUES ('favorite-local', '{"items":[{"template_id":"local-card","type":"Item","size":"Small"}]}',
                    '{"items":[]}', '{"items":[]}', '{"items":[]}');
                INSERT INTO battles (battle_id, source, remote_battle_id, uploader_account_id,
                    local_player_account_id, recorded_at_ms, combat_kind, ghost_replay_state)
                VALUES ('favorite-ghost', 'GHOST', 'remote-ghost', 'uploader', 'local-account',
                    1767225600000, 'PVPCombat', 'remote_available');
                """
            );
            Repository = new HistoryPanelRepository(_databasePath);
            _local = Repository.ListBattles(RunId, new()).Rows.Single();
            _ghost = Repository
                .ListGhostBattles(
                    Account,
                    HistoryPanelDataService.RecorderOutcome(GhostBattleFilter.All),
                    false,
                    new()
                )
                .Rows.Single();
            var replayDirectory = Path.Combine(root, "CombatReplays");
            CopyDirectory = FavoriteBattlePayloadStore.ResolveDirectory(replayDirectory);
            Originals = new CombatReplayPayloadStore(replayDirectory);
            Originals.Save(Copy(LocalId).ReplayPayload);
            Copies = new FavoriteBattlePayloadStore(CopyDirectory);
            LoadGhost = (_, _, _) => Task.FromResult(GhostReplay(5));
            Service = new HistoryFavoriteService(
                Repository,
                replayDirectory,
                LoadLocal,
                (battle, account, token) => LoadGhost(battle, account, token),
                (out string reason) =>
                {
                    reason = string.Empty;
                    return true;
                },
                (_, payload, recordVideo, source) =>
                {
                    LastReplay = new Playback(payload, recordVideo, source);
                    return true;
                }
            );
        }

        internal Task<FavoriteAddResult> AddLocal() =>
            Service.AddAsync(_local, Account, CancellationToken.None);

        internal Task<FavoriteAddResult> AddGhost() =>
            Service.AddAsync(_ghost, Account, CancellationToken.None);

        internal HistoryFavoriteRecord Favorite(string id) =>
            Service.LoadPage(new()).Rows.Single(row => row.BattleId == id);

        internal string CopyPath(string id) =>
            Path.Combine(CopyDirectory, id + ".favorite.mpack.gz");

        internal void AgeCopy(string id) =>
            File.SetLastWriteTimeUtc(CopyPath(id), DateTime.UtcNow.AddHours(-2));

        internal static FavoriteBattlePayload Copy(string id) =>
            new()
            {
                BattleId = id,
                BattleManifest = new PvpBattleManifest { BattleId = id },
                ReplayPayload = new PvpReplayPayload
                {
                    BattleId = id,
                    SpawnMessageBytes = [1],
                    CombatMessageBytes = [2, 3],
                },
            };

        internal GhostReplayLoad GhostReplay(byte marker) =>
            new(
                new GhostBattlePayload
                {
                    BattleId = GhostId,
                    PerspectiveVersion = 1,
                    BattleManifest = new PvpBattleManifest
                    {
                        BattleId = GhostId,
                        Snapshots = new PvpBattleSnapshots
                        {
                            PlayerHand = new PvpBattleCardSetCapture
                            {
                                Items =
                                [
                                    new PvpBattleCardSnapshot
                                    {
                                        TemplateId = "ghost-card-" + marker,
                                    },
                                ],
                            },
                        },
                    },
                    ReplayPayload = new PvpReplayPayload
                    {
                        BattleId = GhostId,
                        SpawnMessageBytes = [1],
                        CombatMessageBytes = [marker],
                    },
                },
                default
            );

        private SavedBattleLoadOutcome LoadLocal(
            string id,
            out PvpBattleManifest? manifest,
            out PvpReplayPayload? payload
        )
        {
            LocalLoads++;
            LocalLoadThread = Environment.CurrentManagedThreadId;
            var loaded = Originals.LoadDetailed(id);
            manifest = LocalManifest;
            payload = loaded.Payload;
            AfterLocalLoad?.Invoke();
            return
                LocalOutcome == SavedBattleLoadOutcome.Loaded
                && loaded.Status != FileBackedPayloadLoadStatus.Loaded
                ? SavedBattleLoadOutcome.Missing
                : LocalOutcome;
        }

        internal SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();
            return connection;
        }

        internal void Execute(string sql)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
