#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelRepository
{
    private const string RecentGhostSyncScopePrefix = "recent_against_me";
    private static readonly TimeSpan GhostRetentionWindow = TimeSpan.FromDays(14);
    private static readonly object SocketEffectTemplateLock = new();
    private static readonly Dictionary<
        (Guid TemplateId, int Tier),
        ECardAttributeType?
    > SocketEffectAttributeTypeCache = new();
    private static object? _staticGameData;

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        Converters = new List<JsonConverter> { new StringEnumConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
    };

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
                        HistoryBattleSource.Local,
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

    public IReadOnlyList<HistoryBattleRecord> ListRecentGhostBattles(
        string localPlayerAccountId,
        int limit
    )
    {
        if (!DatabaseExists || string.IsNullOrWhiteSpace(localPlayerAccountId))
            return Array.Empty<HistoryBattleRecord>();

        MarkOldUndownloadedGhostBattlesDeleted(localPlayerAccountId, DateTimeOffset.UtcNow);

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
                replay_available,
                replay_downloaded
            FROM {RunLogSqliteSchema.BattlesTableName}
            WHERE source = 'GHOST'
              AND local_player_account_id = $localPlayerAccountId
              AND deleted_at_utc IS NULL
            ORDER BY recorded_at_utc DESC, battle_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$localPlayerAccountId", localPlayerAccountId);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var records = new List<HistoryBattleRecord>();
        while (reader.Read())
        {
            var battleId = SafeGetNullableString(reader, "battle_id") ?? "unknown";
            records.Add(
                new HistoryBattleRecord(
                    battleId,
                    string.Empty,
                    DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("recorded_at_utc"))),
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
                    string.Empty,
                    BuildEmptyPreviewData(),
                    HistoryBattleSource.Ghost,
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

    public void MarkOldUndownloadedGhostBattlesDeleted(
        string localPlayerAccountId,
        DateTimeOffset nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            return;

        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BattlesTableName}
            SET deleted_at_utc = COALESCE(deleted_at_utc, $deletedAtUtc)
            WHERE source = 'GHOST'
              AND local_player_account_id = $localPlayerAccountId
              AND replay_downloaded = 0
              AND deleted_at_utc IS NULL
              AND recorded_at_utc < $staleCutoffUtc;
            """;
        command.Parameters.AddWithValue("$localPlayerAccountId", localPlayerAccountId);
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

    public void MarkGhostReplayDownloaded(string localPlayerAccountId, string battleId)
    {
        if (string.IsNullOrWhiteSpace(localPlayerAccountId) || string.IsNullOrWhiteSpace(battleId))
            return;

        using var connection = OpenConnection(ensureSchema: true);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BattlesTableName}
            SET replay_downloaded = 1
            WHERE source = 'GHOST'
              AND local_player_account_id = $localPlayerAccountId
              AND battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$localPlayerAccountId", localPlayerAccountId);
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
            using var bootstrap = connection.CreateCommand();
            bootstrap.CommandTimeout = 2;
            bootstrap.CommandText = RunLogSqliteSchema.BootstrapSql;
            bootstrap.ExecuteNonQuery();
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

    private static string BuildSnapshotSummary(
        PvpBattleCardSetCapture playerHand,
        PvpBattleCardSetCapture playerSkills,
        PvpBattleCardSetCapture opponentHand,
        PvpBattleCardSetCapture opponentSkills
    )
    {
        var playerItems = CountSnapshotItems(playerHand);
        var playerSkillCount = CountSnapshotItems(playerSkills);
        var opponentItems = CountSnapshotItems(opponentHand);
        var opponentSkillCount = CountSnapshotItems(opponentSkills);
        return $"YOU {playerItems} {Pluralize(playerItems, "item", "items")} · {playerSkillCount} {Pluralize(playerSkillCount, "skill", "skills")}"
            + $"  |  OPP {opponentItems} {Pluralize(opponentItems, "item", "items")} · {opponentSkillCount} {Pluralize(opponentSkillCount, "skill", "skills")}";
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }

    private static HistoryBattlePreviewData BuildEmptyPreviewData()
    {
        return new HistoryBattlePreviewData(
            new PreviewBoardModel
            {
                ItemCards = new List<PreviewCardSpec>(),
                SkillCards = new List<PreviewCardSpec>(),
                Metadata = new Dictionary<string, string>(),
                Signature = string.Empty,
            },
            new PreviewBoardModel
            {
                ItemCards = new List<PreviewCardSpec>(),
                SkillCards = new List<PreviewCardSpec>(),
                Metadata = new Dictionary<string, string>(),
                Signature = string.Empty,
            }
        );
    }

    private static HistoryBattlePreviewData BuildPreviewData(
        PvpBattleCardSetCapture playerHand,
        PvpBattleCardSetCapture playerSkills,
        PvpBattleCardSetCapture opponentHand,
        PvpBattleCardSetCapture opponentSkills
    )
    {
        var playerBoard = BuildPreviewBoard(playerHand, playerSkills);
        var opponentBoard = BuildPreviewBoard(opponentHand, opponentSkills);
        return new HistoryBattlePreviewData(playerBoard, opponentBoard);
    }

    private static PreviewBoardModel BuildPreviewBoard(
        PvpBattleCardSetCapture itemCapture,
        PvpBattleCardSetCapture skillCapture
    )
    {
        var itemSnapshots = itemCapture?.Items;
        var socketEffectsBySocket = BuildSocketEffectMap(itemSnapshots);
        var model = new PreviewBoardModel
        {
            ItemCards = PreviewCardSpecFilter.FilterLocallyRenderable(
                BuildPreviewCardSpecs(itemSnapshots, isSkill: false, socketEffectsBySocket)
            ),
            SkillCards = PreviewCardSpecFilter.FilterLocallyRenderable(
                BuildPreviewCardSpecs(skillCapture?.Items, isSkill: true, null)
            ),
            Metadata = new Dictionary<string, string>(),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return model;
    }

    private static List<PreviewCardSpec> BuildPreviewCardSpecs(
        IEnumerable<CombatReplayCardSnapshot>? snapshots,
        bool isSkill,
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        var specs = new List<PreviewCardSpec>();
        if (snapshots == null)
            return specs;

        foreach (
            var snapshot in snapshots
                .Select((snapshot, index) => new { snapshot, index })
                .OrderBy(entry => entry.snapshot?.Socket.HasValue == true ? 0 : 1)
                .ThenBy(entry =>
                    entry.snapshot?.Socket.HasValue == true
                        ? (int)entry.snapshot.Socket!.Value
                        : int.MaxValue
                )
                .ThenBy(entry => entry.index)
                .Select(entry => entry.snapshot)
        )
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;

            var spec = BuildPreviewCardSpec(snapshot, isSkill, socketEffectsBySocket);
            if (spec != null)
                specs.Add(spec);
        }

        return specs;
    }

    private static PreviewCardSpec? BuildPreviewCardSpec(
        CombatReplayCardSnapshot snapshot,
        bool isSkill,
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
            return null;

        if (isSkill)
        {
            if (snapshot.Type != ECardType.Skill)
                return null;
        }
        else if (snapshot.Type != ECardType.Item)
            return null;

        var attributes = new Dictionary<int, int>();
        if (snapshot.Attributes != null)
        {
            foreach (var pair in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        pair.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                {
                    attributes[(int)attributeType] = pair.Value;
                }
            }
        }

        if (!isSkill)
            ApplySocketEffectAttributes(snapshot, attributes, socketEffectsBySocket);

        return new PreviewCardSpec
        {
            TemplateId = snapshot.TemplateId,
            SourceName = snapshot.Name ?? string.Empty,
            Tier = ParseTier(snapshot.Tier),
            Size = isSkill ? 1 : ParseSize(snapshot.Size),
            Enchant = string.IsNullOrWhiteSpace(snapshot.Enchant) ? "None" : snapshot.Enchant!,
            Attributes = attributes,
        };
    }

    private static IReadOnlyDictionary<
        EContainerSocketId,
        HashSet<ECardAttributeType>
    > BuildSocketEffectMap(IEnumerable<CombatReplayCardSnapshot>? snapshots)
    {
        var result = new Dictionary<EContainerSocketId, HashSet<ECardAttributeType>>();
        if (snapshots == null)
            return result;

        foreach (var snapshot in snapshots)
        {
            if (
                snapshot == null
                || snapshot.Type != ECardType.SocketEffect
                || !snapshot.Socket.HasValue
                || string.IsNullOrWhiteSpace(snapshot.TemplateId)
            )
                continue;

            var effectType = ResolveSocketEffectAttributeType(snapshot);
            if (!effectType.HasValue)
                continue;

            if (!result.TryGetValue(snapshot.Socket.Value, out var effects))
            {
                effects = new HashSet<ECardAttributeType>();
                result[snapshot.Socket.Value] = effects;
            }

            effects.Add(effectType.Value);
        }

        return result;
    }

    private static void ApplySocketEffectAttributes(
        CombatReplayCardSnapshot snapshot,
        IDictionary<int, int> attributes,
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        if (
            snapshot == null
            || attributes == null
            || socketEffectsBySocket == null
            || !snapshot.Socket.HasValue
        )
            return;

        foreach (var socket in EnumerateOccupiedSockets(snapshot.Socket.Value, snapshot.Size))
        {
            if (!socketEffectsBySocket.TryGetValue(socket, out var effectTypes))
                continue;

            foreach (var effectType in effectTypes)
            {
                var key = (int)effectType;
                if (!attributes.TryGetValue(key, out var currentValue) || currentValue <= 0)
                    attributes[key] = 1;
            }
        }
    }

    private static IEnumerable<EContainerSocketId> EnumerateOccupiedSockets(
        EContainerSocketId startSocket,
        ECardSize size
    )
    {
        var span = ParseSize(size);
        var start = Math.Max(0, (int)startSocket);
        var end = Math.Min(9, start + Math.Max(1, span) - 1);
        for (var value = start; value <= end; value++)
            yield return (EContainerSocketId)value;
    }

    private static ECardAttributeType? ResolveSocketEffectAttributeType(
        CombatReplayCardSnapshot snapshot
    )
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
            return null;

        if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
            return null;

        var tier = ParseTier(snapshot.Tier);
        var cacheKey = (templateId, tier);
        lock (SocketEffectTemplateLock)
        {
            if (SocketEffectAttributeTypeCache.TryGetValue(cacheKey, out var cachedType))
                return cachedType;
        }

        ECardAttributeType? resolvedType = null;
        try
        {
            var staticData = GetStaticGameData();
            var template = GetTemplate(staticData, templateId) as TCardSocketEffect;
            if (template != null)
            {
                var auras = template.GetAuraTemplatesByTier((ETier)Math.Max(0, tier));
                foreach (var aura in auras)
                {
                    if (
                        aura?.Action is TAuraActionCardModifyAttribute action
                        && (
                            action.AttributeType == ECardAttributeType.Heated
                            || action.AttributeType == ECardAttributeType.Chilled
                        )
                    )
                    {
                        resolvedType = action.AttributeType;
                        break;
                    }
                }
            }
        }
        catch { }

        lock (SocketEffectTemplateLock)
            SocketEffectAttributeTypeCache[cacheKey] = resolvedType;

        return resolvedType;
    }

    private static object? GetStaticGameData()
    {
        lock (SocketEffectTemplateLock)
        {
            if (_staticGameData != null)
                return _staticGameData;
        }

        var staticData = Data.GetStatic().GetAwaiter().GetResult();
        lock (SocketEffectTemplateLock)
        {
            _staticGameData ??= staticData;
            return _staticGameData;
        }
    }

    private static object? GetTemplate(object? staticData, Guid templateId)
    {
        if (staticData == null)
            return null;

        var method = staticData
            .GetType()
            .GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        return method?.Invoke(staticData, new object[] { templateId });
    }

    private static PvpBattleCardSetCapture DeserializeCapture(string json)
    {
        return JsonConvert.DeserializeObject<PvpBattleCardSetCapture>(json, SerializerSettings)
            ?? new PvpBattleCardSetCapture();
    }

    private static int ParseTier(string? value)
    {
        return
            !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<ETier>(value, ignoreCase: false, out var tier)
            ? (int)tier
            : 0;
    }

    private static int ParseSize(ECardSize size)
    {
        return size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };
    }

    private static int CountSnapshotItems(PvpBattleCardSetCapture? capture)
    {
        return capture?.Items?.Count ?? 0;
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

internal sealed class HistoryRunRecord
{
    public HistoryRunRecord(
        string runId,
        string hero,
        string gameMode,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset lastSeenAtUtc,
        int? finalDay,
        int? finalHour,
        int? maxHealth,
        int? prestige,
        int? level,
        int? income,
        int? gold,
        string? playerRank,
        int? playerRating,
        int? victories,
        int? losses,
        string rawStatus,
        int battleCount
    )
    {
        RunId = runId;
        Hero = hero;
        GameMode = gameMode;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
        FinalDay = finalDay;
        FinalHour = finalHour;
        MaxHealth = maxHealth;
        Prestige = prestige;
        Level = level;
        Income = income;
        Gold = gold;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        Victories = victories;
        Losses = losses;
        RawStatus = rawStatus;
        BattleCount = battleCount;
    }

    public string RunId { get; }

    public string Hero { get; }

    public string GameMode { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? EndedAtUtc { get; }

    public DateTimeOffset LastSeenAtUtc { get; }

    public int? FinalDay { get; }

    public int? FinalHour { get; }

    public int? MaxHealth { get; }

    public int? Prestige { get; }

    public int? Level { get; }

    public int? Income { get; }

    public int? Gold { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public int? Victories { get; }

    public int? Losses { get; }

    public string RawStatus { get; }

    public int BattleCount { get; }
}

internal sealed class HistoryBattleRecord
{
    public HistoryBattleRecord(
        string battleId,
        string runId,
        DateTimeOffset recordedAtUtc,
        int? day,
        int? hour,
        string? encounterId,
        string? playerHero,
        string? playerRank,
        int? playerRating,
        int? playerLevel,
        string? opponentName,
        string? opponentHero,
        string? opponentRank,
        int? opponentRating,
        int? opponentLevel,
        string? opponentAccountId,
        string? combatKind,
        string? result,
        string? winnerCombatantId,
        string? loserCombatantId,
        string snapshotSummary,
        HistoryBattlePreviewData previewData,
        HistoryBattleSource source,
        bool replayAvailable,
        bool replayDownloaded
    )
    {
        BattleId = battleId;
        RunId = runId;
        RecordedAtUtc = recordedAtUtc;
        Day = day;
        Hour = hour;
        EncounterId = encounterId;
        PlayerHero = playerHero;
        PlayerRank = playerRank;
        PlayerRating = playerRating;
        PlayerLevel = playerLevel;
        OpponentName = opponentName;
        OpponentHero = opponentHero;
        OpponentRank = opponentRank;
        OpponentRating = opponentRating;
        OpponentLevel = opponentLevel;
        OpponentAccountId = opponentAccountId;
        CombatKind = combatKind;
        Result = result;
        WinnerCombatantId = winnerCombatantId;
        LoserCombatantId = loserCombatantId;
        SnapshotSummary = snapshotSummary;
        PreviewData = previewData;
        Source = source;
        ReplayAvailable = replayAvailable;
        ReplayDownloaded = replayDownloaded;
    }

    public string BattleId { get; }

    public string RunId { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public int? Day { get; }

    public int? Hour { get; }

    public string? EncounterId { get; }

    public string? PlayerHero { get; }

    public string? PlayerRank { get; }

    public int? PlayerRating { get; }

    public int? PlayerLevel { get; }

    public string? OpponentName { get; }

    public string? OpponentHero { get; }

    public string? OpponentRank { get; }

    public int? OpponentRating { get; }

    public int? OpponentLevel { get; }

    public string? OpponentAccountId { get; }

    public string? CombatKind { get; }

    public string? Result { get; }

    public string? WinnerCombatantId { get; }

    public string? LoserCombatantId { get; }

    public string SnapshotSummary { get; }

    public HistoryBattlePreviewData PreviewData { get; }

    public HistoryBattleSource Source { get; }

    public bool ReplayAvailable { get; }

    public bool ReplayDownloaded { get; }
}

internal enum HistoryBattleSource
{
    Local,
    Ghost,
}

internal sealed class HistoryBattlePreviewData
{
    private static readonly PreviewBoardModel EmptyBoard = new PreviewBoardModel
    {
        ItemCards = new List<PreviewCardSpec>(),
        SkillCards = new List<PreviewCardSpec>(),
        Metadata = new Dictionary<string, string>(),
        Signature = string.Empty,
    };

    public HistoryBattlePreviewData(PreviewBoardModel playerBoard, PreviewBoardModel opponentBoard)
    {
        PlayerBoard = playerBoard ?? CloneBoard(EmptyBoard);
        OpponentBoard = opponentBoard ?? CloneBoard(EmptyBoard);
    }

    public PreviewBoardModel PlayerBoard { get; }

    public PreviewBoardModel OpponentBoard { get; }

    public bool HasRenderablePlayerBoard => CountRenderableCards(PlayerBoard) > 0;

    public bool HasRenderableOpponentBoard => CountRenderableCards(OpponentBoard) > 0;

    public bool HasRenderableCards => HasRenderablePlayerBoard || HasRenderableOpponentBoard;

    public HistoryBattlePreviewData PlayerOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(PlayerBoard), CloneBoard(EmptyBoard));
    }

    public HistoryBattlePreviewData OpponentOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(EmptyBoard), CloneBoard(OpponentBoard));
    }

    public HistoryBattlePreviewData PlayerHandOnly()
    {
        return new HistoryBattlePreviewData(CloneItemBoard(PlayerBoard), CloneBoard(EmptyBoard));
    }

    public HistoryBattlePreviewData OpponentHandOnly()
    {
        return new HistoryBattlePreviewData(CloneBoard(EmptyBoard), CloneItemBoard(OpponentBoard));
    }

    private static int CountRenderableCards(PreviewBoardModel board)
    {
        if (board == null)
            return 0;

        return (board.ItemCards?.Count ?? 0) + (board.SkillCards?.Count ?? 0);
    }

    private static PreviewBoardModel CloneBoard(PreviewBoardModel source)
    {
        return new PreviewBoardModel
        {
            ItemCards =
                source?.ItemCards != null
                    ? new List<PreviewCardSpec>(source.ItemCards)
                    : new List<PreviewCardSpec>(),
            SkillCards =
                source?.SkillCards != null
                    ? new List<PreviewCardSpec>(source.SkillCards)
                    : new List<PreviewCardSpec>(),
            Metadata =
                source?.Metadata != null
                    ? new Dictionary<string, string>(source.Metadata)
                    : new Dictionary<string, string>(),
            Signature = source?.Signature ?? string.Empty,
        };
    }

    private static PreviewBoardModel CloneItemBoard(PreviewBoardModel source)
    {
        return new PreviewBoardModel
        {
            ItemCards =
                source?.ItemCards != null
                    ? new List<PreviewCardSpec>(source.ItemCards)
                    : new List<PreviewCardSpec>(),
            SkillCards = new List<PreviewCardSpec>(),
            Metadata =
                source?.Metadata != null
                    ? new Dictionary<string, string>(source.Metadata)
                    : new Dictionary<string, string>(),
            Signature = source?.Signature ?? string.Empty,
        };
    }
}
