#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal sealed class PvpBattleSqliteStore : SqlitePersistenceStoreBase
{
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

    public PvpBattleSqliteStore(string databasePath)
        : base(databasePath) { }

    public void Save(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifest.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(manifest));
        if (!string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal))
            return;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var persistedRunId = ResolvePersistedRunId(connection, transaction, manifest.RunId);
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.BattlesTableName} (
                battle_id,
                source,
                run_id,
                has_local_payload,
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
                loser_combatant_id
            ) VALUES (
                $battleId,
                'LOCAL',
                $runId,
                1,
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
                $loserCombatantId
            )
            ON CONFLICT(battle_id) DO UPDATE SET
                source = 'LOCAL',
                run_id = excluded.run_id,
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
                has_local_payload = 1;
            """;
        command.Parameters.AddWithValue("$battleId", manifest.BattleId);
        command.Parameters.AddWithValue("$runId", (object?)persistedRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recordedAtUtc", manifest.RecordedAtUtc.ToString("o"));
        AddNullableInt32(command, "$day", manifest.Day);
        AddNullableInt32(command, "$hour", manifest.Hour);
        command.Parameters.AddWithValue(
            "$encounterId",
            (object?)manifest.EncounterId ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$playerName",
            (object?)manifest.Participants.PlayerName ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$playerAccountId",
            (object?)manifest.Participants.PlayerAccountId ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$playerHero",
            (object?)manifest.Participants.PlayerHero ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$playerRank",
            (object?)manifest.Participants.PlayerRank ?? DBNull.Value
        );
        AddNullableInt32(command, "$playerRating", manifest.Participants.PlayerRating);
        AddNullableInt32(command, "$playerLevel", manifest.Participants.PlayerLevel);
        command.Parameters.AddWithValue(
            "$opponentName",
            (object?)manifest.Participants.OpponentName ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$opponentHero",
            (object?)manifest.Participants.OpponentHero ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$opponentRank",
            (object?)manifest.Participants.OpponentRank ?? DBNull.Value
        );
        AddNullableInt32(command, "$opponentRating", manifest.Participants.OpponentRating);
        AddNullableInt32(command, "$opponentLevel", manifest.Participants.OpponentLevel);
        command.Parameters.AddWithValue(
            "$opponentAccountId",
            (object?)manifest.Participants.OpponentAccountId ?? DBNull.Value
        );
        command.Parameters.AddWithValue("$combatKind", manifest.CombatKind);
        command.Parameters.AddWithValue(
            "$result",
            (object?)manifest.Outcome.Result ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$winnerCombatantId",
            (object?)manifest.Outcome.WinnerCombatantId ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$loserCombatantId",
            (object?)manifest.Outcome.LoserCombatantId ?? DBNull.Value
        );
        command.ExecuteNonQuery();

        using var snapshotCommand = CreateCommand(connection, transaction);
        snapshotCommand.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.BattleSnapshotsTableName} (
                battle_id,
                player_hand_json,
                player_skills_json,
                opponent_hand_json,
                opponent_skills_json
            ) VALUES (
                $battleId,
                $playerHandJson,
                $playerSkillsJson,
                $opponentHandJson,
                $opponentSkillsJson
            )
            ON CONFLICT(battle_id) DO UPDATE SET
                player_hand_json = excluded.player_hand_json,
                player_skills_json = excluded.player_skills_json,
                opponent_hand_json = excluded.opponent_hand_json,
                opponent_skills_json = excluded.opponent_skills_json;
            """;
        snapshotCommand.Parameters.AddWithValue("$battleId", manifest.BattleId);
        snapshotCommand.Parameters.AddWithValue(
            "$playerHandJson",
            SerializeCapture(manifest.Snapshots.PlayerHand)
        );
        snapshotCommand.Parameters.AddWithValue(
            "$playerSkillsJson",
            SerializeCapture(manifest.Snapshots.PlayerSkills)
        );
        snapshotCommand.Parameters.AddWithValue(
            "$opponentHandJson",
            SerializeCapture(manifest.Snapshots.OpponentHand)
        );
        snapshotCommand.Parameters.AddWithValue(
            "$opponentSkillsJson",
            SerializeCapture(manifest.Snapshots.OpponentSkills)
        );
        snapshotCommand.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string? ResolvePersistedRunId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? runId
    )
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            SELECT 1
            FROM {RunLogSqliteSchema.RunsTableName}
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        return command.ExecuteScalar() == null ? null : runId;
    }

    public PvpBattleManifest? TryLoad(string battleId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                b.battle_id,
                b.run_id,
                b.recorded_at_utc,
                b.day,
                b.hour,
                b.encounter_id,
                b.player_name,
                b.player_account_id,
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
            WHERE b.battle_id = $battleId
              AND b.source = 'LOCAL'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return ReadManifest(reader);
    }

    public void Delete(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return;

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText =
            $"DELETE FROM {RunLogSqliteSchema.BattlesTableName} WHERE battle_id = $battleId;";
        command.Parameters.AddWithValue("$battleId", battleId);
        command.ExecuteNonQuery();
    }

    public void AttachToRun(string battleId, string runId)
    {
        if (string.IsNullOrWhiteSpace(battleId) || string.IsNullOrWhiteSpace(runId))
            return;

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BattlesTableName}
            SET run_id = $runId
            WHERE battle_id = $battleId
              AND source = 'LOCAL'
              AND (run_id IS NULL OR run_id = $runId)
              AND EXISTS (
                  SELECT 1
                  FROM {RunLogSqliteSchema.RunsTableName}
                  WHERE run_id = $runId
                  LIMIT 1
              );
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    public IEnumerable<string> ListBattleIds()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT battle_id
            FROM {RunLogSqliteSchema.BattlesTableName}
            WHERE source = 'LOCAL'
            ORDER BY recorded_at_utc DESC, battle_id DESC;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return reader.GetString(0);
        }
    }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                b.battle_id,
                b.run_id,
                b.recorded_at_utc,
                b.day,
                b.hour,
                b.encounter_id,
                b.player_name,
                b.player_account_id,
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
            WHERE b.source = 'LOCAL'
            ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var manifests = new List<PvpBattleManifest>();
        while (reader.Read())
        {
            manifests.Add(ReadManifest(reader));
        }

        return manifests;
    }

    public IReadOnlyList<PvpBattleManifest> ListByRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return Array.Empty<PvpBattleManifest>();

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                b.battle_id,
                b.run_id,
                b.recorded_at_utc,
                b.day,
                b.hour,
                b.encounter_id,
                b.player_name,
                b.player_account_id,
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
            WHERE b.source = 'LOCAL'
              AND b.run_id = $runId
            ORDER BY b.recorded_at_utc ASC, b.battle_id ASC;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        var manifests = new List<PvpBattleManifest>();
        while (reader.Read())
            manifests.Add(ReadManifest(reader));

        return manifests;
    }

    private static string SerializeCapture(PvpBattleCardSetCapture capture)
    {
        return JsonConvert.SerializeObject(capture, SerializerSettings);
    }

    private static PvpBattleCardSetCapture DeserializeCapture(string json)
    {
        return JsonConvert.DeserializeObject<PvpBattleCardSetCapture>(json, SerializerSettings)
            ?? new PvpBattleCardSetCapture();
    }

    private static PvpBattleManifest ReadManifest(SqliteDataReader reader)
    {
        return new PvpBattleManifest
        {
            BattleId = reader.GetString(reader.GetOrdinal("battle_id")),
            RunId = GetNullableString(reader, "run_id"),
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
}
