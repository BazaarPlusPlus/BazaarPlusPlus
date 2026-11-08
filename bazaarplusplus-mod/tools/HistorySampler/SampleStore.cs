using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.HistorySampler;

/// <summary>A file the tool wrote or deleted, relative to the Data Root.</summary>
internal readonly record struct DataRootChange(bool Written, string RelativePath);

/// <summary>What one import or clear did to a Data Root.</summary>
internal sealed class SampleResult
{
    public List<string> RunIds { get; } = new();

    public int Battles { get; set; }

    public int Replays { get; set; }

    public List<DataRootChange> Files { get; } = new();
}

/// <summary>Who the sampled runs belong to: the local player, never the uploader.</summary>
internal sealed record LocalPlayer(string AccountId, string Name);

/// <summary>
/// Writes sampled runs into one Data Root's history database as History-Only Runs and removes
/// them again. A sampled run is marked twice: <c>upload_policy='history_only'</c> keeps it out of
/// sealing, protection and resume, and <c>mod_version='sample:…'</c> tells it apart from any
/// other History-Only Run so <see cref="Clear"/> touches nothing else.
/// </summary>
internal sealed class SampleStore
{
    public const string ModVersionPrefix = "sample:";
    public const string RunIdPrefix = "sample-";
    private const string FallbackName = "Sample";

    private readonly string _dataRoot;
    private readonly string _databasePath;
    private readonly SqliteOpenOptions _options;

    public SampleStore(string dataRoot, SqliteOpenOptions options)
    {
        _dataRoot = Path.GetFullPath(dataRoot);
        _databasePath = PathConstants.RunLogDatabase(_dataRoot);
        _options = options;
        if (!File.Exists(_databasePath))
            throw new InvalidOperationException(
                $"No history database at {_databasePath}; start the game with the mod once first."
            );
    }

    /// <summary>The newest local run's account and the name the local player last fought under.</summary>
    public LocalPlayer ResolveLocalPlayer(string? accountOverride)
    {
        using var connection = Open();
        var account =
            accountOverride
            ?? Scalar(
                connection,
                $"""
                SELECT player_account_id FROM {RunLogSchema.RunsTableName}
                WHERE player_account_id IS NOT NULL AND player_account_id <> ''
                    AND (mod_version IS NULL OR mod_version NOT LIKE '{ModVersionPrefix}%')
                ORDER BY started_at_ms DESC LIMIT 1;
                """
            );
        if (string.IsNullOrWhiteSpace(account))
            throw new InvalidOperationException(
                "The database has no local run with an account; pass --account <id>."
            );
        var name = Scalar(
            connection,
            $"""
            SELECT player_name FROM {RunLogSchema.BattlesTableName}
            WHERE source = 'LOCAL' AND player_account_id = $account
                AND player_name IS NOT NULL AND player_name <> ''
            ORDER BY recorded_at_ms DESC LIMIT 1;
            """,
            ("$account", account)
        );
        return new LocalPlayer(account!, string.IsNullOrWhiteSpace(name) ? FallbackName : name!);
    }

    /// <summary>
    /// Copies one opened Bundle's run and its PvP battles. The run row commits first: the battle
    /// store attaches a battle only to a run it can already read.
    /// </summary>
    public bool Import(OpenedRunBundleV6 bundle, LocalPlayer player, SampleResult result)
    {
        var payload = bundle.Payload;
        var battles = (payload.Battles ?? new List<RunBattleV6>())
            .Where(battle => battle != null)
            .ToList();
        var runId = RunIdPrefix + ShortHash(bundle.Bundle.Manifest.BundleId);
        using (var connection = Open())
        {
            if (Scalar(connection, RunExistsSql, ("$runId", runId)) != null)
                return false;
            InsertRun(connection, runId, payload.Run, player.AccountId);
        }

        var store = new PvpBattleSqliteStore(_databasePath, _options);
        var catalog = new PvpBattleCatalog(store);
        var replays = new CombatReplayPayloadStore(PathConstants.CombatReplays(_dataRoot));
        var missing = new List<string>();
        for (var index = 0; index < battles.Count; index++)
        {
            var battle = battles[index];
            var battleId = $"{runId}-{index:D2}";
            var manifest = GhostManifestProjection.BuildUploaderManifest(
                battleId,
                runId,
                battle,
                CardEnumEvidence.Instance
            );
            // A local battle is a PvP combat; the uploader is the local player here.
            manifest.CombatKind = "PVPCombat";
            manifest.Participants.PlayerAccountId = player.AccountId;
            manifest.Participants.PlayerName = player.Name;
            catalog.Save(manifest);
            result.Battles++;
            if (bundle.TryGetReplayableBattle(battle.BattleId, out var replayable))
            {
                replays.Save(
                    new PvpReplayPayload
                    {
                        BattleId = battleId,
                        Version = replayable!.Replay!.Version,
                        SpawnMessageBytes = replayable.Replay.SpawnMessageBytes.ToArray(),
                        CombatMessageBytes = replayable.Replay.CombatMessageBytes.ToArray(),
                        DespawnMessageBytes = replayable.Replay.DespawnMessageBytes.ToArray(),
                    }
                );
                result.Replays++;
                result.Files.Add(new DataRootChange(true, ReplayRelativePath(battleId)));
            }
            else
                missing.Add(battleId);
        }
        if (missing.Count > 0)
            store.MarkReplayPayloadMissing(missing, DateTimeOffset.UtcNow);
        result.RunIds.Add(runId);
        return true;
    }

    /// <summary>
    /// Removes every sampled run with its battles, snapshots, favorites, recorded videos and replay
    /// files. Favorites and videos are copies with no foreign key to the battle, so they go by id.
    /// </summary>
    public SampleResult Clear()
    {
        var result = new SampleResult();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var runIds = Column(
            connection,
            transaction,
            $"SELECT run_id FROM {RunLogSchema.RunsTableName} WHERE {SampledRun};"
        );
        var battleIds = Column(
            connection,
            transaction,
            $"""
            SELECT battle_id FROM {RunLogSchema.BattlesTableName}
            WHERE run_id IN (SELECT run_id FROM {RunLogSchema.RunsTableName} WHERE {SampledRun});
            """
        );
        var videos = Column(
            connection,
            transaction,
            $"SELECT video_relative_path FROM combat_replay_videos WHERE battle_id LIKE '{RunIdPrefix}%';"
        );
        Execute(
            connection,
            transaction,
            $"DELETE FROM favorite_battles WHERE battle_id LIKE '{RunIdPrefix}%';"
        );
        Execute(
            connection,
            transaction,
            $"DELETE FROM combat_replay_videos WHERE battle_id LIKE '{RunIdPrefix}%';"
        );
        Execute(
            connection,
            transaction,
            $"DELETE FROM {RunLogSchema.BattleSnapshotsTableName} WHERE battle_id IN (SELECT battle_id FROM {RunLogSchema.BattlesTableName} WHERE run_id IN (SELECT run_id FROM {RunLogSchema.RunsTableName} WHERE {SampledRun}));"
        );
        Execute(
            connection,
            transaction,
            $"DELETE FROM {RunLogSchema.BattlesTableName} WHERE run_id IN (SELECT run_id FROM {RunLogSchema.RunsTableName} WHERE {SampledRun});"
        );
        Execute(
            connection,
            transaction,
            $"DELETE FROM {RunLogSchema.RunsTableName} WHERE {SampledRun};"
        );
        transaction.Commit();

        result.RunIds.AddRange(runIds);
        result.Battles = battleIds.Count;
        foreach (var battleId in battleIds)
        {
            DeleteFile(ReplayRelativePath(battleId), result);
            DeleteFile(
                Path.Combine(
                    Path.GetFileName(PathConstants.FavoriteBattles(_dataRoot)),
                    battleId + ".favorite.mpack.gz"
                ),
                result
            );
        }
        foreach (var video in videos)
            DeleteFile(
                Path.Combine(Path.GetFileName(PathConstants.CombatReplayVideos(_dataRoot)), video),
                result
            );
        return result;
    }

    private const string RunExistsSql =
        $"SELECT 1 FROM {RunLogSchema.RunsTableName} WHERE run_id = $runId LIMIT 1;";

    private static readonly string SampledRun =
        $"upload_policy = 'history_only' AND mod_version LIKE '{ModVersionPrefix}%' AND run_id LIKE '{RunIdPrefix}%'";

    private static string ReplayRelativePath(string battleId) =>
        Path.Combine(
            Path.GetFileName(PathConstants.CombatReplays("/")),
            battleId + ".payload.mpack.gz"
        );

    private void DeleteFile(string relative, SampleResult result)
    {
        var path = Path.GetFullPath(Path.Combine(_dataRoot, relative));
        if (!path.StartsWith(_dataRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return;
        if (File.Exists(path))
            File.Delete(path);
        result.Files.Add(new DataRootChange(false, relative));
    }

    // The run as the Bundle recorded it, under a new id; a run the Bundle left active reads as
    // abandoned, because nothing will ever resume it.
    private static void InsertRun(
        SqliteConnection connection,
        string runId,
        RunFactsV6 run,
        string accountId
    )
    {
        var status = run.Status?.Trim().ToLowerInvariant() switch
        {
            "completed" => "completed",
            _ => run.EndedAtMs.HasValue ? "completed" : "abandoned",
        };
        var lastSeen = run.EndedAtMs ?? run.StartedAtMs;
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.RunsTableName} (
                run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode,
                player_rank, player_rating, day, hour, max_health, prestige, level, income, gold,
                ended_at_ms, final_day, final_hour, victories, losses,
                final_player_rank, final_player_rating, final_player_rating_delta,
                build_channel, player_account_id, mod_version, upload_policy
            ) VALUES (
                $runId, $startedAt, $lastSeen, $status, $hero, $mode,
                $rank, $rating, $day, $hour, $maxHealth, $prestige, $level, $income, $gold,
                $endedAt, $day, $hour, $victories, $losses,
                $finalRank, $finalRating, $finalRatingDelta,
                $buildChannel, $account, $modVersion, 'history_only'
            );
            """;
        void Add(string name, object? value) =>
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        Add("$runId", runId);
        Add("$startedAt", run.StartedAtMs);
        Add("$lastSeen", lastSeen);
        Add("$status", status);
        Add("$hero", run.Hero ?? string.Empty);
        Add("$mode", run.GameMode ?? string.Empty);
        Add("$rank", run.PlayerRank);
        Add("$rating", run.PlayerRating);
        Add("$day", run.Day);
        Add("$hour", run.Hour);
        Add("$maxHealth", run.MaxHealth);
        Add("$prestige", run.Prestige);
        Add("$level", run.Level);
        Add("$income", run.Income);
        Add("$gold", run.Gold);
        Add("$endedAt", run.EndedAtMs);
        Add("$victories", run.Victories);
        Add("$losses", run.Losses);
        Add("$finalRank", run.FinalPlayerRank);
        Add("$finalRating", run.FinalPlayerRating);
        Add("$finalRatingDelta", run.FinalPlayerRatingDelta);
        Add("$buildChannel", run.BuildChannel);
        Add("$account", accountId);
        Add("$modVersion", ModVersionPrefix + (run.ModVersion ?? string.Empty));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        // The store creates or migrates the schema exactly as the mod does before any raw read.
        _ = new PvpBattleSqliteStore(_databasePath, _options);
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString()
        );
        connection.Open();
        Execute(
            connection,
            null,
            _options.JournalMode == SqliteJournalMode.Wal
                ? "PRAGMA journal_mode = WAL;"
                : "PRAGMA journal_mode = DELETE;"
        );
        Execute(connection, null, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static string? Scalar(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteScalar() switch
        {
            string text => text,
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static List<string> Column(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
            if (!reader.IsDBNull(0))
                values.Add(reader.GetString(0));
        return values;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ShortHash(string value) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12]
            .ToLowerInvariant();
}
