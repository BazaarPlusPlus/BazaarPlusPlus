#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelRepository
{
    private const string RecentGhostSyncScopePrefix = "recent_against_me";
    private static readonly TimeSpan GhostRetentionWindow = TimeSpan.FromDays(14);

    private readonly string _databasePath;

    public HistoryPanelRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
    }

    public bool DatabaseExists => File.Exists(_databasePath);

    public IReadOnlyList<HistoryRunRecord> ListRecentRuns(int limit)
    {
        if (!DatabaseExists)
            return Array.Empty<HistoryRunRecord>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                r.run_id,
                r.hero,
                r.game_mode,
                r.started_at_utc,
                r.last_seen_at_utc,
                r.player_rank,
                r.player_rating,
                r.status AS run_status,
                r.day,
                r.hour,
                r.final_day,
                r.final_hour,
                r.max_health AS final_max_health,
                r.prestige AS final_prestige,
                r.level AS final_level,
                r.income AS final_income,
                r.gold AS final_gold,
                r.victories,
                r.losses,
                r.ended_at_utc,
                COUNT(s.battle_id) AS battle_count
            FROM {RunLogSqliteSchema.RunsTableName} AS r
            LEFT JOIN {RunLogSqliteSchema.BattlesTableName} AS pb
                ON pb.run_id = r.run_id
               AND pb.source = 'LOCAL'
            LEFT JOIN {RunLogSqliteSchema.BattleSnapshotsTableName} AS s
                ON s.battle_id = pb.battle_id
               AND s.player_hand_json IS NOT NULL
               AND s.player_skills_json IS NOT NULL
               AND s.opponent_hand_json IS NOT NULL
               AND s.opponent_skills_json IS NOT NULL
               AND json_valid(s.player_hand_json) = 1
               AND json_valid(s.player_skills_json) = 1
               AND json_valid(s.opponent_hand_json) = 1
               AND json_valid(s.opponent_skills_json) = 1
            GROUP BY
                r.run_id,
                r.hero,
                r.game_mode,
                r.started_at_utc,
                r.last_seen_at_utc,
                r.player_rank,
                r.player_rating,
                r.status,
                r.day,
                r.hour,
                r.final_day,
                r.final_hour,
                r.max_health,
                r.prestige,
                r.level,
                r.income,
                r.gold,
                r.victories,
                r.losses,
                r.ended_at_utc
            ORDER BY
                COALESCE(r.ended_at_utc, r.last_seen_at_utc, r.started_at_utc) DESC,
                r.run_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryRunRecord>();
        while (reader.Read())
        {
            var startedAt = DateTimeOffset.Parse(
                reader.GetString(reader.GetOrdinal("started_at_utc"))
            );
            var endedAt = GetNullableDateTimeOffset(reader, "ended_at_utc");
            var victories = GetNullableInt32(reader, "victories");
            var losses = GetNullableInt32(reader, "losses");
            var finalDay = GetNullableInt32(reader, "final_day") ?? GetNullableInt32(reader, "day");
            var finalHour =
                GetNullableInt32(reader, "final_hour") ?? GetNullableInt32(reader, "hour");
            var lastSeen =
                endedAt ?? GetNullableDateTimeOffset(reader, "last_seen_at_utc") ?? startedAt;
            var rawStatus = reader.GetString(reader.GetOrdinal("run_status"));

            records.Add(
                new HistoryRunRecord(
                    reader.GetString(reader.GetOrdinal("run_id")),
                    reader.GetString(reader.GetOrdinal("hero")),
                    reader.GetString(reader.GetOrdinal("game_mode")),
                    startedAt,
                    endedAt,
                    lastSeen,
                    finalDay,
                    finalHour,
                    GetNullableInt32(reader, "final_max_health"),
                    GetNullableInt32(reader, "final_prestige"),
                    GetNullableInt32(reader, "final_level"),
                    GetNullableInt32(reader, "final_income"),
                    GetNullableInt32(reader, "final_gold"),
                    GetNullableString(reader, "player_rank"),
                    GetNullableInt32(reader, "player_rating"),
                    victories,
                    losses,
                    rawStatus,
                    reader.GetInt32(reader.GetOrdinal("battle_count"))
                )
            );
        }

        return records;
    }

    public IReadOnlyList<HistoryBattleRecord> ListBattlesByRun(string runId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(runId))
            return Array.Empty<HistoryBattleRecord>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                b.battle_id,
                b.run_id,
                b.recorded_at_utc,
                b.day,
                b.hour,
                b.encounter_id,
                b.player_hero,
                b.player_rank,
                b.player_rating,
                b.player_level,
                b.opponent_name,
                b.opponent_hero,
                b.opponent_rank,
                b.opponent_rating,
                b.opponent_level,
                b.opponent_account_id,
                b.combat_kind,
                b.result,
                b.winner_combatant_id,
                b.loser_combatant_id,
                s.player_hand_json,
                s.player_skills_json,
                s.opponent_hand_json,
                s.opponent_skills_json
            FROM {RunLogSqliteSchema.BattlesTableName} AS b
            LEFT JOIN {RunLogSqliteSchema.BattleSnapshotsTableName} AS s
                ON s.battle_id = b.battle_id
            WHERE b.run_id = $runId
              AND b.source = 'LOCAL'
            ORDER BY b.recorded_at_utc DESC, b.battle_id DESC;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryBattleRecord>();
        while (reader.Read())
        {
            var battleId = SafeGetNullableString(reader, "battle_id") ?? "unknown";
            try
            {
                var playerHand = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("player_hand_json"))
                );
                var playerSkills = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("player_skills_json"))
                );
                var opponentHand = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("opponent_hand_json"))
                );
                var opponentSkills = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("opponent_skills_json"))
                );

                records.Add(
                    new HistoryBattleRecord(
                        battleId,
                        reader.GetString(reader.GetOrdinal("run_id")),
                        DateTimeOffset.Parse(
                            reader.GetString(reader.GetOrdinal("recorded_at_utc"))
                        ),
                        GetNullableInt32(reader, "day"),
                        GetNullableInt32(reader, "hour"),
                        GetNullableString(reader, "encounter_id"),
                        GetNullableString(reader, "player_hero"),
                        GetNullableString(reader, "player_rank"),
                        GetNullableInt32(reader, "player_rating"),
                        GetNullableInt32(reader, "player_level"),
                        GetNullableString(reader, "opponent_name"),
                        GetNullableString(reader, "opponent_hero"),
                        GetNullableString(reader, "opponent_rank"),
                        GetNullableInt32(reader, "opponent_rating"),
                        GetNullableInt32(reader, "opponent_level"),
                        GetNullableString(reader, "opponent_account_id"),
                        GetNullableString(reader, "combat_kind"),
                        GetNullableString(reader, "result"),
                        GetNullableString(reader, "winner_combatant_id"),
                        GetNullableString(reader, "loser_combatant_id"),
                        BuildSnapshotSummary(
                            playerHand,
                            playerSkills,
                            opponentHand,
                            opponentSkills
                        ),
                        BuildPreviewData(playerHand, playerSkills, opponentHand, opponentSkills),
                        isBundleFinalBattle: false,
                        source: HistoryBattleSource.Local,
                        replayAvailable: true,
                        replayDownloaded: true
                    )
                );
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "HistoryPanelRepository",
                    $"Skipping unreadable battle history row '{battleId}': {ex.Message}"
                );
            }
        }

        return records;
    }

    public IReadOnlyList<HistoryBattleRecord> ListRecentGhostBattles(int limit)
    {
        if (!DatabaseExists)
            return Array.Empty<HistoryBattleRecord>();

        MarkOldUndownloadedGhostBattlesDeleted(DateTimeOffset.UtcNow);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                battle_id,
                local_player_account_id,
                recorded_at_utc,
                day,
                hour,
                encounter_id,
                player_name,
                player_account_id,
                player_hero,
                player_rank,
                player_rating,
                player_level,
                opponent_name,
                opponent_hero,
                opponent_rank,
                opponent_rating,
                opponent_level,
                opponent_account_id,
                combat_kind,
                result,
                winner_combatant_id,
                loser_combatant_id,
                is_bundle_final_battle,
                replay_available,
                replay_downloaded
            FROM {RunLogSqliteSchema.BattlesTableName}
            WHERE source = 'GHOST'
              AND deleted_at_utc IS NULL
            ORDER BY recorded_at_utc DESC, battle_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryBattleRecord>();
        while (reader.Read())
        {
            var battleId = SafeGetNullableString(reader, "battle_id") ?? "unknown";
            records.Add(
                GhostBattleLocalProjector.CreateHistoryBattleRecord(
                    battleId,
                    DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("recorded_at_utc"))),
                    GetNullableInt32(reader, "day"),
                    GetNullableInt32(reader, "hour"),
                    GetNullableString(reader, "encounter_id"),
                    GetNullableString(reader, "player_name"),
                    GetNullableString(reader, "player_account_id"),
                    GetNullableString(reader, "player_hero"),
                    GetNullableString(reader, "player_rank"),
                    GetNullableInt32(reader, "player_rating"),
                    GetNullableInt32(reader, "player_level"),
                    GetNullableString(reader, "opponent_hero"),
                    GetNullableString(reader, "opponent_rank"),
                    GetNullableInt32(reader, "opponent_rating"),
                    GetNullableInt32(reader, "opponent_level"),
                    GetNullableString(reader, "combat_kind"),
                    GetNullableString(reader, "result"),
                    GetNullableString(reader, "winner_combatant_id"),
                    GetNullableString(reader, "loser_combatant_id"),
                    isBundleFinalBattle: GetNullableInt32(reader, "is_bundle_final_battle") == 1,
                    replayAvailable: GetNullableInt32(reader, "replay_available") == 1,
                    replayDownloaded: GetNullableInt32(reader, "replay_downloaded") == 1
                )
            );
        }

        return records;
    }

    public void ReplaceGhostBattles(
        string localPlayerAccountId,
        IReadOnlyList<GhostBattleImportRecord> battles
    )
    {
        UpsertGhostBattles(localPlayerAccountId, battles);
    }

    public void UpsertGhostBattles(
        string localPlayerAccountId,
        IReadOnlyList<GhostBattleImportRecord> battles
    )
    {
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            throw new ArgumentException(
                "Local player account id is required.",
                nameof(localPlayerAccountId)
            );

        using var connection = OpenConnection(ensureSchema: true);
        using var transaction = connection.BeginTransaction();

        foreach (var battle in battles)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandTimeout = 2;
            insertCommand.CommandText = $"""
                INSERT INTO {RunLogSqliteSchema.BattlesTableName} (
                    battle_id,
                    source,
                    run_id,
                    local_player_account_id,
                    recorded_at_utc,
                    day,
                    hour,
                    encounter_id,
                    player_name,
                    player_account_id,
                    player_hero,
                    player_rank,
                    player_rating,
                    player_level,
                    opponent_name,
                    opponent_hero,
                    opponent_rank,
                    opponent_rating,
                    opponent_level,
                    opponent_account_id,
                    combat_kind,
                    result,
                    winner_combatant_id,
                    loser_combatant_id,
                    is_bundle_final_battle,
                    replay_available,
                    replay_downloaded,
                    last_synced_at_utc
                ) VALUES (
                    $battleId,
                    'GHOST',
                    NULL,
                    $localPlayerAccountId,
                    $recordedAtUtc,
                    $day,
                    $hour,
                    $encounterId,
                    $playerName,
                    $playerAccountId,
                    $playerHero,
                    $playerRank,
                    $playerRating,
                    $playerLevel,
                    $opponentName,
                    $opponentHero,
                    $opponentRank,
                    $opponentRating,
                    $opponentLevel,
                    $opponentAccountId,
                    $combatKind,
                    $result,
                    $winnerCombatantId,
                    $loserCombatantId,
                    $isBundleFinalBattle,
                    $replayAvailable,
                    $replayDownloaded,
                    $lastSyncedAtUtc
                )
                ON CONFLICT(battle_id) DO UPDATE SET
                    source = 'GHOST',
                    run_id = NULL,
                    local_player_account_id = excluded.local_player_account_id,
                    recorded_at_utc = excluded.recorded_at_utc,
                    day = excluded.day,
                    hour = excluded.hour,
                    encounter_id = excluded.encounter_id,
                    player_name = excluded.player_name,
                    player_account_id = excluded.player_account_id,
                    player_hero = excluded.player_hero,
                    player_rank = excluded.player_rank,
                    player_rating = excluded.player_rating,
                    player_level = excluded.player_level,
                    opponent_name = excluded.opponent_name,
                    opponent_hero = excluded.opponent_hero,
                    opponent_rank = excluded.opponent_rank,
                    opponent_rating = excluded.opponent_rating,
                    opponent_level = excluded.opponent_level,
                    opponent_account_id = excluded.opponent_account_id,
                    combat_kind = excluded.combat_kind,
                    result = excluded.result,
                    winner_combatant_id = excluded.winner_combatant_id,
                    loser_combatant_id = excluded.loser_combatant_id,
                    is_bundle_final_battle = excluded.is_bundle_final_battle,
                    replay_available = excluded.replay_available,
                    replay_downloaded = MAX(
                        {RunLogSqliteSchema.GhostBattlesTableName}.replay_downloaded,
                        excluded.replay_downloaded
                    ),
                    last_synced_at_utc = excluded.last_synced_at_utc,
                    deleted_at_utc = CASE
                        WHEN excluded.recorded_at_utc >= $staleCutoffUtc THEN NULL
                        ELSE {RunLogSqliteSchema.BattlesTableName}.deleted_at_utc
                    END;
                """;
            insertCommand.Parameters.AddWithValue("$battleId", battle.BattleId);
            insertCommand.Parameters.AddWithValue("$localPlayerAccountId", localPlayerAccountId);
            insertCommand.Parameters.AddWithValue(
                "$staleCutoffUtc",
                DateTimeOffset.UtcNow.Subtract(GhostRetentionWindow).ToString("o")
            );
            insertCommand.Parameters.AddWithValue(
                "$recordedAtUtc",
                battle.RecordedAtUtc.ToString("o")
            );
            insertCommand.Parameters.AddWithValue("$day", (object?)battle.Day ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$hour", (object?)battle.Hour ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue(
                "$encounterId",
                (object?)battle.EncounterId ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$playerName",
                (object?)battle.PlayerName ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$playerAccountId",
                (object?)battle.PlayerAccountId ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$playerHero",
                (object?)battle.PlayerHero ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$playerRank",
                (object?)battle.PlayerRank ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$playerRating",
                (object?)battle.PlayerRating ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$playerLevel",
                (object?)battle.PlayerLevel ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$opponentName",
                (object?)battle.OpponentName ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$opponentHero",
                (object?)battle.OpponentHero ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$opponentRank",
                (object?)battle.OpponentRank ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$opponentRating",
                (object?)battle.OpponentRating ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$opponentLevel",
                (object?)battle.OpponentLevel ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$opponentAccountId",
                (object?)battle.OpponentAccountId ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue("$combatKind", battle.CombatKind);
            insertCommand.Parameters.AddWithValue(
                "$result",
                (object?)battle.Result ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$winnerCombatantId",
                (object?)battle.WinnerCombatantId ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$loserCombatantId",
                (object?)battle.LoserCombatantId ?? DBNull.Value
            );
            insertCommand.Parameters.AddWithValue(
                "$isBundleFinalBattle",
                battle.IsBundleFinalBattle ? 1 : 0
            );
            insertCommand.Parameters.AddWithValue(
                "$replayAvailable",
                battle.ReplayAvailable ? 1 : 0
            );
            insertCommand.Parameters.AddWithValue(
                "$replayDownloaded",
                battle.ReplayDownloaded ? 1 : 0
            );
            insertCommand.Parameters.AddWithValue(
                "$lastSyncedAtUtc",
                battle.LastSyncedAtUtc.ToString("o")
            );
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void MarkOldUndownloadedGhostBattlesDeleted(DateTimeOffset nowUtc)
    {
        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BattlesTableName}
            SET deleted_at_utc = COALESCE(deleted_at_utc, $deletedAtUtc)
            WHERE source = 'GHOST'
              AND replay_downloaded = 0
              AND deleted_at_utc IS NULL
              AND recorded_at_utc < $staleCutoffUtc;
            """;
        command.Parameters.AddWithValue("$deletedAtUtc", nowUtc.ToString("o"));
        command.Parameters.AddWithValue(
            "$staleCutoffUtc",
            nowUtc.Subtract(GhostRetentionWindow).ToString("o")
        );
        command.ExecuteNonQuery();
    }

    public DateTimeOffset? TryGetGhostSyncCheckpointUtc(string localPlayerAccountId)
    {
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            return null;

        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT cursor_value
            FROM {RunLogSqliteSchema.SyncCursorsTableName}
            WHERE scope = $scope
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scope", BuildGhostSyncScope(localPlayerAccountId));
        var rawValue = command.ExecuteScalar() as string;
        return DateTimeOffset.TryParse(rawValue, out var parsed) ? parsed : null;
    }

    public void SaveGhostSyncCheckpointUtc(string localPlayerAccountId, DateTimeOffset syncedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            throw new ArgumentException(
                "Local player account id is required.",
                nameof(localPlayerAccountId)
            );

        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.SyncCursorsTableName} (
                scope,
                cursor_value,
                updated_at_utc
            ) VALUES (
                $scope,
                $syncedAtUtc,
                $syncedAtUtc
            )
            ON CONFLICT(scope) DO UPDATE SET
                cursor_value = excluded.cursor_value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$scope", BuildGhostSyncScope(localPlayerAccountId));
        command.Parameters.AddWithValue("$syncedAtUtc", syncedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void MarkGhostReplayDownloaded(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return;

        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BattlesTableName}
            SET replay_downloaded = 1
            WHERE source = 'GHOST'
              AND battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> ListBattleIdsByRun(string runId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(runId))
            return Array.Empty<string>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT battle_id
            FROM {RunLogSqliteSchema.BattlesTableName}
            WHERE run_id = $runId
              AND source = 'LOCAL'
            ORDER BY recorded_at_utc DESC, battle_id DESC;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        var battleIds = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                battleIds.Add(reader.GetString(0));
        }

        return battleIds;
    }

    public void DeleteRun(string runId)
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(runId))
            return;

        using var connection = OpenConnection();
        using var deleteRun = connection.CreateCommand();
        deleteRun.CommandTimeout = 2;
        deleteRun.CommandText =
            $"DELETE FROM {RunLogSqliteSchema.RunsTableName} WHERE run_id = $runId;";
        deleteRun.Parameters.AddWithValue("$runId", runId);
        deleteRun.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection(bool ensureSchema = false)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        if (ensureSchema)
        {
            RunLogSqliteSchema.EnsureInitialized(connection);
            EnsureColumnExists(
                connection,
                RunLogSqliteSchema.BattlesTableName,
                "is_bundle_final_battle",
                "INTEGER NOT NULL DEFAULT 0"
            );
        }
        return connection;
    }

    private static string BuildGhostSyncScope(string localPlayerAccountId)
    {
        return $"{RecentGhostSyncScopePrefix}::{localPlayerAccountId.Trim()}";
    }

    private static PvpBattleManifest ReadManifest(SqliteDataReader reader, string? runIdColumnName)
    {
        return new PvpBattleManifest
        {
            BattleId = reader.GetString(reader.GetOrdinal("battle_id")),
            RunId = string.IsNullOrWhiteSpace(runIdColumnName)
                ? null
                : GetNullableString(reader, runIdColumnName),
            RecordedAtUtc = DateTimeOffset.Parse(
                reader.GetString(reader.GetOrdinal("recorded_at_utc"))
            ),
            Day = GetNullableInt32(reader, "day"),
            Hour = GetNullableInt32(reader, "hour"),
            EncounterId = GetNullableString(reader, "encounter_id"),
            CombatKind = reader.GetString(reader.GetOrdinal("combat_kind")),
            Participants = new PvpBattleParticipants
            {
                PlayerName = GetNullableString(reader, "player_name"),
                PlayerAccountId = GetNullableString(reader, "player_account_id"),
                PlayerHero = GetNullableString(reader, "player_hero"),
                PlayerRank = GetNullableString(reader, "player_rank"),
                PlayerRating = GetNullableInt32(reader, "player_rating"),
                PlayerLevel = GetNullableInt32(reader, "player_level"),
                OpponentName = GetNullableString(reader, "opponent_name"),
                OpponentHero = GetNullableString(reader, "opponent_hero"),
                OpponentRank = GetNullableString(reader, "opponent_rank"),
                OpponentRating = GetNullableInt32(reader, "opponent_rating"),
                OpponentLevel = GetNullableInt32(reader, "opponent_level"),
                OpponentAccountId = GetNullableString(reader, "opponent_account_id"),
            },
            Outcome = new PvpBattleOutcome
            {
                Result = GetNullableString(reader, "result"),
                WinnerCombatantId = GetNullableString(reader, "winner_combatant_id"),
                LoserCombatantId = GetNullableString(reader, "loser_combatant_id"),
            },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("player_hand_json"))
                ),
                PlayerSkills = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("player_skills_json"))
                ),
                OpponentHand = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("opponent_hand_json"))
                ),
                OpponentSkills = DeserializeCapture(
                    reader.GetString(reader.GetOrdinal("opponent_skills_json"))
                ),
            },
        };
    }

    private static void EnsureColumnExists(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition
    )
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandTimeout = 2;
            exists.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $tableName LIMIT 1;";
            exists.Parameters.AddWithValue("$tableName", tableName);
            if (exists.ExecuteScalar() == null)
                return;
        }

        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (
                string.Equals(
                    reader.GetString(reader.GetOrdinal("name")),
                    columnName,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandTimeout = 2;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? SafeGetNullableString(SqliteDataReader reader, string columnName)
    {
        try
        {
            return GetNullableString(reader, columnName);
        }
        catch
        {
            return null;
        }
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(
        SqliteDataReader reader,
        string columnName
    )
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    }
}
