#nullable enable
using System.Globalization;
using BazaarPlusPlus.Storage;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal sealed class PvpBattleSqliteStore : SqliteStoreBase, IPvpBattleCatalog
{
    private static readonly JsonSerializerSettings SerializerSettings =
        SerializerSettingsFactory.CreateSerializerSettings(includeStringEnumConverter: true);
    private readonly Action<ReplayPayloadMaintenanceStorageEvent>? _maintenanceDiagnostics;

    // Shared column list + battle/snapshot join used by every manifest read. Callers append only
    // their WHERE/ORDER/LIMIT clauses. Column order is fixed because ReadManifest depends on it.
    private static readonly string SelectBattleManifestSql = $"""
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
            b.player_prestige,
            b.player_income,
            b.player_gold,
            b.player_victories,
            b.opponent_name,
            b.opponent_hero,
            b.opponent_rank,
            b.opponent_rating,
            b.opponent_level,
            b.opponent_prestige,
            b.opponent_victories,
            b.opponent_account_id,
            b.combat_kind,
            b.result,
            b.winner_combatant_id,
            b.loser_combatant_id,
            s.player_hand_json,
            s.player_skills_json,
            s.opponent_hand_json,
            s.opponent_skills_json
        FROM {RunLogSchema.BattlesTableName} AS b
        LEFT JOIN {RunLogSchema.BattleSnapshotsTableName} AS s
            ON s.battle_id = b.battle_id
        """;

    public PvpBattleSqliteStore(
        string databasePath,
        Action<ReplayPayloadMaintenanceStorageEvent>? maintenanceDiagnostics = null
    )
        : base(databasePath)
    {
        _maintenanceDiagnostics = maintenanceDiagnostics;
    }

    public void Save(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifest.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(manifest));
        if (!string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal))
            throw new ArgumentException(
                "Only PVPCombat manifests can be saved to the PvP battle catalog.",
                nameof(manifest)
            );

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var persistedRunId = ResolvePersistedRunId(connection, transaction, manifest.RunId);
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.BattlesTableName} (
                battle_id,
                source,
                run_id,
                has_local_payload,
                local_payload_state,
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
                player_prestige,
                player_income,
                player_gold,
                player_victories,
                opponent_name,
                opponent_hero,
                opponent_rank,
                opponent_rating,
                opponent_level,
                opponent_prestige,
                opponent_victories,
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
                'ready',
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
                $playerPrestige,
                $playerIncome,
                $playerGold,
                $playerVictories,
                $opponentName,
                $opponentHero,
                $opponentRank,
                $opponentRating,
                $opponentLevel,
                $opponentPrestige,
                $opponentVictories,
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
                player_prestige = excluded.player_prestige,
                player_income = excluded.player_income,
                player_gold = excluded.player_gold,
                player_victories = excluded.player_victories,
                opponent_name = excluded.opponent_name,
                opponent_hero = excluded.opponent_hero,
                opponent_rank = excluded.opponent_rank,
                opponent_rating = excluded.opponent_rating,
                opponent_level = excluded.opponent_level,
                opponent_prestige = excluded.opponent_prestige,
                opponent_victories = excluded.opponent_victories,
                opponent_account_id = excluded.opponent_account_id,
                combat_kind = excluded.combat_kind,
                result = excluded.result,
                winner_combatant_id = excluded.winner_combatant_id,
                loser_combatant_id = excluded.loser_combatant_id,
                has_local_payload = 1,
                local_payload_state = 'ready',
                local_payload_maintenance_at_utc = NULL;
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
        AddNullableInt32(command, "$playerPrestige", manifest.Participants.PlayerPrestige);
        AddNullableInt32(command, "$playerIncome", manifest.Participants.PlayerIncome);
        AddNullableInt32(command, "$playerGold", manifest.Participants.PlayerGold);
        AddNullableInt32(command, "$playerVictories", manifest.Participants.PlayerVictories);
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
        AddNullableInt32(command, "$opponentPrestige", manifest.Participants.OpponentPrestige);
        AddNullableInt32(command, "$opponentVictories", manifest.Participants.OpponentVictories);
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
            INSERT INTO {RunLogSchema.BattleSnapshotsTableName} (
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
            FROM {RunLogSchema.RunsTableName}
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
        command.CommandText =
            SelectBattleManifestSql
            + "\nWHERE b.battle_id = $battleId\n  AND b.source = 'LOCAL'\nLIMIT 1;";
        command.Parameters.AddWithValue("$battleId", battleId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return ReadManifest(reader);
    }

    public void AttachToRun(string battleId, string runId)
    {
        if (string.IsNullOrWhiteSpace(battleId) || string.IsNullOrWhiteSpace(runId))
            return;

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BattlesTableName}
            SET run_id = $runId
            WHERE battle_id = $battleId
              AND source = 'LOCAL'
              AND (run_id IS NULL OR run_id = $runId)
              AND EXISTS (
                  SELECT 1
                  FROM {RunLogSchema.RunsTableName}
                  WHERE run_id = $runId
                  LIMIT 1
              );
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory()
    {
        using var connection = OpenMaintenanceConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT battle_id, recorded_at_utc, has_local_payload, local_payload_state
            FROM {RunLogSchema.BattlesTableName}
            WHERE source = 'LOCAL'
            ORDER BY recorded_at_utc DESC, battle_id DESC;
            """;
        using var reader = command.ExecuteReader();
        ObserveMaintenanceCommand();
        var records = new List<ReplayPayloadMaintenanceRecord>();
        while (reader.Read())
        {
            records.Add(
                new ReplayPayloadMaintenanceRecord(
                    reader.GetString(0),
                    DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    reader.GetInt32(2) == 1,
                    ParsePayloadState(reader.GetString(3))
                )
            );
        }
        return records;
    }

    public IReadOnlyList<string> ScheduleReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    )
    {
        var normalized = NormalizeBattleIds(battleIds);
        if (normalized.Count == 0)
            return Array.Empty<string>();

        using var connection = OpenMaintenanceConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        var proposedIdsJson = JsonConvert.SerializeObject(normalized);
        using var select = CreateCommand(connection, transaction);
        select.CommandText = $"""
            SELECT b.battle_id
            FROM {RunLogSchema.BattlesTableName} AS b
            WHERE b.source = 'LOCAL'
              AND b.local_payload_state = 'ready'
              AND b.battle_id IN (SELECT value FROM json_each($battleIdsJson))
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSchema.RunsTableName} AS active_run
                  WHERE active_run.run_id = b.run_id
                    AND lower(active_run.status) = 'active'
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSchema.BundleSealJobsTableName} AS seal_job
                  WHERE seal_job.run_id = b.run_id
                    AND seal_job.state IN ('waiting', 'sealing')
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSchema.BundleOutboxTableName} AS active_outbox
                  WHERE active_outbox.run_id = b.run_id
                    AND active_outbox.status = 'pending'
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSchema.RunsTableName} AS eligible_run
                  WHERE eligible_run.run_id = b.run_id
                    AND eligible_run.completed = 1
                    AND lower(eligible_run.status) = 'completed'
                    AND lower(eligible_run.game_mode) = 'ranked'
                    AND lower(COALESCE(eligible_run.build_channel, 'unknown')) <> 'ptr'
                    AND NOT EXISTS (
                        SELECT 1
                        FROM {RunLogSchema.BundleSealJobsTableName} AS existing_seal_job
                        WHERE existing_seal_job.run_id = eligible_run.run_id
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM {RunLogSchema.BundleOutboxTableName} AS uploaded_outbox
                        WHERE uploaded_outbox.run_id = eligible_run.run_id
                          AND uploaded_outbox.status = 'uploaded'
                    )
              )
            ORDER BY b.recorded_at_utc DESC, b.battle_id DESC;
            """;
        select.Parameters.AddWithValue("$battleIdsJson", proposedIdsJson);
        var scheduled = new List<string>();
        using (var reader = select.ExecuteReader())
        {
            ObserveMaintenanceCommand();
            while (reader.Read())
                scheduled.Add(reader.GetString(0));
        }

        if (scheduled.Count > 0)
        {
            using var update = CreateCommand(connection, transaction);
            update.CommandText = $"""
                UPDATE {RunLogSchema.BattlesTableName}
                SET has_local_payload = 0,
                    local_payload_state = 'delete_pending',
                    local_payload_maintenance_at_utc = $now
                WHERE source = 'LOCAL'
                  AND local_payload_state = 'ready'
                  AND battle_id IN (SELECT value FROM json_each($scheduledIdsJson));
                """;
            update.Parameters.AddWithValue(
                "$scheduledIdsJson",
                JsonConvert.SerializeObject(scheduled)
            );
            update.Parameters.AddWithValue("$now", now.ToString("o"));
            update.ExecuteNonQuery();
            ObserveMaintenanceCommand();
        }

        transaction.Commit();
        return scheduled;
    }

    public void CompleteReplayPayloadDeletion(
        IReadOnlyCollection<string> battleIds,
        DateTimeOffset now
    )
    {
        UpdatePayloadState(battleIds, expectedState: "delete_pending", targetState: "evicted", now);
    }

    public void MarkReplayPayloadMissing(IReadOnlyCollection<string> battleIds, DateTimeOffset now)
    {
        UpdatePayloadState(battleIds, expectedState: "ready", targetState: "missing", now);
    }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText =
            SelectBattleManifestSql
            + "\nWHERE b.source = 'LOCAL'\nORDER BY b.recorded_at_utc DESC, b.battle_id DESC\nLIMIT $limit;";
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
        command.CommandText =
            SelectBattleManifestSql
            + "\nWHERE b.source = 'LOCAL'\n  AND b.run_id = $runId\nORDER BY b.recorded_at_utc ASC, b.battle_id ASC;";
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        var manifests = new List<PvpBattleManifest>();
        while (reader.Read())
            manifests.Add(ReadManifest(reader));

        return manifests;
    }

    private void UpdatePayloadState(
        IReadOnlyCollection<string> battleIds,
        string expectedState,
        string targetState,
        DateTimeOffset now
    )
    {
        var normalized = NormalizeBattleIds(battleIds);
        if (normalized.Count == 0)
            return;

        using var connection = OpenMaintenanceConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BattlesTableName}
            SET has_local_payload = 0,
                local_payload_state = $targetState,
                local_payload_maintenance_at_utc = $now
            WHERE source = 'LOCAL'
              AND local_payload_state = $expectedState
              AND battle_id IN (SELECT value FROM json_each($battleIdsJson));
            """;
        command.Parameters.AddWithValue("$targetState", targetState);
        command.Parameters.AddWithValue("$expectedState", expectedState);
        command.Parameters.AddWithValue("$now", now.ToString("o"));
        command.Parameters.AddWithValue("$battleIdsJson", JsonConvert.SerializeObject(normalized));
        command.ExecuteNonQuery();
        ObserveMaintenanceCommand();
    }

    private SqliteConnection OpenMaintenanceConnection()
    {
        var connection = OpenConnection();
        _maintenanceDiagnostics?.Invoke(ReplayPayloadMaintenanceStorageEvent.ConnectionOpened);
        return connection;
    }

    private void ObserveMaintenanceCommand()
    {
        _maintenanceDiagnostics?.Invoke(ReplayPayloadMaintenanceStorageEvent.CommandExecuted);
    }

    private static List<string> NormalizeBattleIds(IReadOnlyCollection<string>? battleIds)
    {
        if (battleIds == null || battleIds.Count == 0)
            return [];

        return battleIds
            .Where(battleId => !string.IsNullOrWhiteSpace(battleId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static ReplayPayloadState ParsePayloadState(string value) =>
        value switch
        {
            "ready" => ReplayPayloadState.Ready,
            "delete_pending" => ReplayPayloadState.DeletePending,
            "evicted" => ReplayPayloadState.Evicted,
            "missing" => ReplayPayloadState.Missing,
            _ => throw new InvalidOperationException($"Unknown replay payload state '{value}'."),
        };

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
                reader.GetString(reader.GetOrdinal("recorded_at_utc")),
                CultureInfo.InvariantCulture
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
                PlayerPrestige = GetNullableInt32(reader, "player_prestige"),
                PlayerIncome = GetNullableInt32(reader, "player_income"),
                PlayerGold = GetNullableInt32(reader, "player_gold"),
                PlayerVictories = GetNullableInt32(reader, "player_victories"),
                OpponentName = GetNullableString(reader, "opponent_name"),
                OpponentHero = GetNullableString(reader, "opponent_hero"),
                OpponentRank = GetNullableString(reader, "opponent_rank"),
                OpponentRating = GetNullableInt32(reader, "opponent_rating"),
                OpponentLevel = GetNullableInt32(reader, "opponent_level"),
                OpponentPrestige = GetNullableInt32(reader, "opponent_prestige"),
                OpponentVictories = GetNullableInt32(reader, "opponent_victories"),
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
