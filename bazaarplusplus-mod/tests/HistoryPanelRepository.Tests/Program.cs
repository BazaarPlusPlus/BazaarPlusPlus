#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Models;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), $"bpp-history-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var paths = new TestPaths(root);
    var store = new RunLogStore(paths);
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-delete",
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-local",
            BundleScreenshotRequested = true,
            ModVersion = "5.0.0",
        }
    );
    var databasePath = PathConstants.RunLogDatabase(root);
    InsertPendingOutbox(databasePath, "run-delete");
    InsertRunDeleteArtifacts(databasePath, "run-delete");

    var repository = new HistoryPanelRepository(databasePath);
    Assert(
        repository.ListRuns(new(Limit: 10)).Rows.Single().RunId == "run-delete",
        "Fresh run must be visible."
    );
    var deleteResult = repository.DeleteRun("run-delete");
    Assert(
        repository.ListRuns(new(Limit: 10)).Rows.Count == 0,
        "HistoryPanel DeleteRun must delete the source run."
    );
    Assert(
        ReadScalar(databasePath, "SELECT COUNT(*) FROM bundle_outbox;") == 1,
        "Outbox audit rows must not block or follow DeleteRun."
    );
    Assert(
        deleteResult.BattleIds.SequenceEqual(["delete-battle"])
            && deleteResult.DetachedVideoCount == 1,
        "DeleteRun must atomically return deleted battles and detach their video metadata."
    );
    Assert(
        ReadScalar(
            databasePath,
            "SELECT COUNT(*) FROM combat_replay_videos WHERE video_id='kept-video' AND attachment_state='detached' AND file_state='present';"
        ) == 1,
        "DeleteRun must retain successful MP4 metadata as a discoverable detached artifact."
    );

    var first = Ghost(
        "battle-remote",
        "bundle-a",
        "https://r2.example/old",
        DateTimeOffset.UtcNow.AddMinutes(1)
    );
    repository.UpsertGhostBattles("account-local", [first]);
    var local = repository
        .ListGhostBattles("account-local", GhostBattleFilter.All, false, new())
        .Rows.Single();
    Assert(!local.SnapshotCounts.Known, "Undownloaded Ghost counts must remain unknown.");
    Assert(
        local.Replay == ReplayAvailability.Remote,
        "Ghost rows must carry download state into presentation."
    );
    Assert(!local.IsFinalBattle, "A non-final Ghost must stay non-final.");
    var localId = local.BattleId;

    var newer = Ghost(
        "battle-remote",
        "bundle-b",
        "https://r2.example/new",
        DateTimeOffset.UtcNow.AddMinutes(10)
    );
    repository.UpsertGhostBattles("account-local", [newer]);
    var reference = repository.TryGetGhostBundleReference(localId)!;
    Assert(
        reference.BundleId == "bundle-b"
            && reference.DownloadUrl.EndsWith("/new", StringComparison.Ordinal),
        "Discovery must overwrite the URL, expiry, and bundle identity."
    );
    var final = Ghost(
        "battle-final",
        "bundle-final",
        "https://r2.example/final",
        DateTimeOffset.UtcNow.AddMinutes(10)
    );
    final.IsFinalBattle = true;
    repository.UpsertGhostBattles("account-local", [final]);
    var finalRow = repository
        .ListGhostBattles("account-local", GhostBattleFilter.All, false, new())
        .Rows.Single(row => row.BattleId != localId);
    Assert(finalRow.IsFinalBattle, "Ghost discovery must keep the final-battle fact.");

    repository.MarkGhostReplayUnavailable(localId, ReplayAvailability.Unavailable, "corrupt");
    Assert(
        repository
            .ListGhostBattles("account-local", GhostBattleFilter.All, false, new())
            .Rows.Single(row => row.BattleId == localId)
            .Replay == ReplayAvailability.Unavailable,
        "Ghost rows must distinguish unavailable data from an undownloaded replay."
    );
    repository.MarkGhostReplayUnavailable(
        localId,
        ReplayAvailability.Expired,
        "object_unavailable"
    );
    var expiredRow = repository
        .ListGhostBattles("account-local", GhostBattleFilter.All, false, new())
        .Rows.Single(row => row.BattleId == localId);
    Assert(
        expiredRow.Replay == ReplayAvailability.Expired,
        "Expired rows must retain their specific state without offering download."
    );
    repository.MarkGhostReplayUnavailable(localId, ReplayAvailability.Unavailable, "corrupt");
    repository.UpsertGhostBattles("account-local", [newer]);
    Assert(
        repository.TryGetGhostBundleReference(localId)?.ReplayState
            == ReplayAvailability.Unavailable,
        "Permanent payload failure must survive discovery refresh."
    );

    Assert(
        ReadScalar(
            databasePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('run_sync_state','battle_replay_sync_state','sync_cursors','sync_checkpoints');"
        ) == 0,
        "V4 sync tables must not exist."
    );

    AssertRecentRunProjection(databasePath);
    HistoryPaginationTests.Run(root);
    HistoryFavoritesTests.Run(root);
    ReplayAvailabilityCodecTests.Run(root);
    HistoryReadSchedulingTests.Run();
    ReplayMaintenanceStorageTests.Run();
    ReplayPayloadRetentionPolicyTests.Run();
    ReplayPayloadOperationGateTests.Run();
    ReplayPayloadMaintenanceServiceTests.Run();
    ReplayVideoMetadataLifecycleTests.Run();
    ReplayVideoArtifactMaintenanceTests.Run();

    Console.WriteLine("History panel repository tests passed.");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

static GhostBattleImportRecord Ghost(
    string battleId,
    string bundleId,
    string url,
    DateTimeOffset expiry
) =>
    new()
    {
        BattleId = battleId,
        BundleId = bundleId,
        DownloadUrl = url,
        DownloadExpiresAtUtc = expiry,
        RecordedAtUtc = DateTimeOffset.UtcNow,
        Day = 3,
        Hour = 4,
        PlayerAccountId = "account-uploader",
        PlayerName = "Uploader",
        OpponentAccountId = "account-local",
        OpponentName = "Local",
        CombatKind = "PVPCombat",
        Result = "unknown",
    };

// Summary pages must remain independent of malformed or absent snapshot documents.
static void AssertRecentRunProjection(string databasePath)
{
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();

    InsertRun(connection, "run-oldest", started: 1767225600000L, ended: null);
    InsertRun(connection, "run-middle", started: 1767312000000L, ended: null);
    // Ends last despite starting first, so a wrong ordering key surfaces here.
    InsertRun(connection, "run-newest", started: 1767268800000L, ended: 1767398400000L);

    // Two countable battles.
    InsertLocalBattle(connection, "b-ok-1", "run-newest");
    const string emptyCapture =
        "{\"Items\":[],\"Status\":\"CapturedEmpty\",\"Source\":\"Unknown\"}";
    InsertSnapshot(connection, "b-ok-1", emptyCapture, emptyCapture, emptyCapture, emptyCapture);
    InsertLocalBattle(connection, "b-ok-2", "run-newest");
    InsertSnapshot(connection, "b-ok-2", "[1]", "[2]", "[3]", "[4]");
    // Malformed snapshot document: present but not countable.
    InsertLocalBattle(connection, "b-bad-json", "run-newest");
    InsertSnapshot(connection, "b-bad-json", "[]", "not json", "[]", "[]");
    // Battle without any snapshot row: not countable.
    InsertLocalBattle(connection, "b-no-snapshot", "run-newest");
    // Ghost battles never belong to a run and must never be counted.
    InsertGhostBattle(connection, "b-ghost");
    InsertSnapshot(connection, "b-ghost", "[]", "[]", "[]", "[]");

    InsertLocalBattle(connection, "b-middle", "run-middle");
    InsertSnapshot(connection, "b-middle", "[]", "[]", "[]", "[]");

    var repository = new HistoryPanelRepository(databasePath);

    var localBattles = repository.ListBattles("run-newest", new()).Rows;
    Assert(
        localBattles.Count > 0
            && localBattles.All(battle => battle.Replay == ReplayAvailability.Unavailable),
        "History must project evicted local payloads as unavailable instead of hard-coding replay eligibility."
    );

    Assert(
        localBattles
            .Select(b => b.BattleId)
            .OrderBy(id => id)
            .SequenceEqual(["b-bad-json", "b-no-snapshot", "b-ok-1", "b-ok-2"]),
        "Battle lists must include facts without decoding snapshots."
    );
    Assert(
        repository.LoadSnapshots("run-newest", "b-ok-1") != null,
        "Selected detail must load its snapshot."
    );
    var all = repository.ListRuns(new(Limit: 10)).Rows;
    Assert(
        all.Select(run => run.RunId).SequenceEqual(["run-newest", "run-middle", "run-oldest"]),
        "Runs must order by ended/last-seen/started recency, newest first."
    );
    Assert(
        all.Single(run => run.RunId == "run-newest").Hero == "Vanessa"
            && all.Single(run => run.RunId == "run-newest").GameMode == "Ranked",
        "Run scalar columns must survive the battle join."
    );

    var limited = repository.ListRuns(new(Limit: 2)).Rows;
    Assert(
        limited.Select(run => run.RunId).SequenceEqual(["run-newest", "run-middle"]),
        "The limit must keep the most recent runs, not an arbitrary pair."
    );

    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            UPDATE runs SET player_rank = 'Diamond 1', player_rating = 1490,
                final_player_rank = 'Legendary', final_player_rating = 1506,
                final_player_rating_delta = 16
            WHERE run_id = 'run-newest';
            UPDATE runs SET player_rank = 'Gold 2', player_rating = 1300
            WHERE run_id = 'run-middle';
            """;
        command.ExecuteNonQuery();
    }
    var ranked = repository.ListRuns(new(Limit: 10)).Rows;
    var finished = ranked.Single(run => run.RunId == "run-newest");
    Assert(
        finished.PlayerRank == "Legendary"
            && finished.PlayerRating == 1506
            && finished.PlayerRatingDelta == 16,
        "A finished run must show the rank, rating and change it ended on."
    );
    var unfinished = ranked.Single(run => run.RunId == "run-middle");
    Assert(
        unfinished.PlayerRank == "Gold 2"
            && unfinished.PlayerRating == 1300
            && unfinished.PlayerRatingDelta == null,
        "A run without final values must fall back to its starting rank and rating."
    );
}

static void InsertRun(SqliteConnection connection, string runId, long started, long? ended)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO runs (
            run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode, ended_at_ms
        ) VALUES ($runId, $started, $started, 'completed', 'Vanessa', 'Ranked', $ended);
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$started", started);
    command.Parameters.AddWithValue("$ended", (object?)ended ?? DBNull.Value);
    command.ExecuteNonQuery();
}

static void InsertLocalBattle(SqliteConnection connection, string battleId, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO battles (
            battle_id, source, run_id, recorded_at_ms, combat_kind,
            local_payload_state
        )
        VALUES (
            $battleId, 'LOCAL', $runId, 1767312000000, 'PVPCombat',
            'missing'
        );
        """;
    command.Parameters.AddWithValue("$battleId", battleId);
    command.Parameters.AddWithValue("$runId", runId);
    command.ExecuteNonQuery();
}

static void InsertGhostBattle(SqliteConnection connection, string battleId)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO battles (
            battle_id, source, remote_battle_id, uploader_account_id, recorded_at_ms, combat_kind
        ) VALUES ($battleId, 'GHOST', 'remote-1', 'account-uploader', 1767312000000, 'PVPCombat');
        """;
    command.Parameters.AddWithValue("$battleId", battleId);
    command.ExecuteNonQuery();
}

static void InsertSnapshot(
    SqliteConnection connection,
    string battleId,
    string playerHand,
    string playerSkills,
    string opponentHand,
    string opponentSkills
)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO battle_snapshots (
            battle_id, player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json
        ) VALUES ($battleId, $playerHand, $playerSkills, $opponentHand, $opponentSkills);
        """;
    command.Parameters.AddWithValue("$battleId", battleId);
    command.Parameters.AddWithValue("$playerHand", playerHand);
    command.Parameters.AddWithValue("$playerSkills", playerSkills);
    command.Parameters.AddWithValue("$opponentHand", opponentHand);
    command.Parameters.AddWithValue("$opponentSkills", opponentSkills);
    command.ExecuteNonQuery();
}

static void InsertPendingOutbox(string databasePath, string runId)
{
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO bundle_outbox (
            bundle_id, run_id, file_name, content_sha256_hex, content_digest,
            total_bytes, has_screenshot, sealed_at_ms, status
        ) VALUES (
            '01K1ABCDEF0123456789ABCDEF', $runId, 'bundle.bundle', $sha, $digest,
            399, 0, $sealed, 'pending'
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$sha", new string('a', 64));
    command.Parameters.AddWithValue(
        "$digest",
        "sha-256=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=:"
    );
    command.Parameters.AddWithValue("$sealed", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    command.ExecuteNonQuery();
}

static void InsertRunDeleteArtifacts(string databasePath, string runId)
{
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO battles (
            battle_id, source, run_id, recorded_at_ms, combat_kind,
            local_payload_state
        ) VALUES (
            'delete-battle', 'LOCAL', $runId, 1787443200000, 'PVPCombat',
            'ready'
        );
        INSERT INTO combat_replay_videos (
            video_id, battle_id, source, video_relative_path,
            width, height, fps, codec, started_at_ms, ended_at_ms,
            captured_frames, dropped_frames, status, file_size_bytes,
            attachment_state, file_state
        ) VALUES (
            'kept-video', 'delete-battle', 'LocalSaved', '2026/kept-video.mp4',
            1920, 1080, 30, 'libx264', 1787443200000, 1787443260000,
            1800, 0, 'COMPLETED', 12345,
            'attached', 'present'
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.ExecuteNonQuery();
}

static long ReadScalar(string databasePath, string sql)
{
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(command.ExecuteScalar());
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class TestPaths(string dataRoot) : IPathProvider
{
    public string? DataRootDirectoryPath { get; } = dataRoot;
    public string? PluginsDirectoryPath => null;
}
