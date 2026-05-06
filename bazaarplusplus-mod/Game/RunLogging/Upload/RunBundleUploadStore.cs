#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Online;
using BazaarPlusPlus.Game.Online.Models;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadStore
{
    private readonly string _databasePath;
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly PvpBattleCatalog _battleCatalog;

    public RunBundleUploadStore(string databasePath, string replayRootPath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        if (string.IsNullOrWhiteSpace(replayRootPath))
            throw new ArgumentException("Replay root path is required.", nameof(replayRootPath));

        _databasePath = databasePath;
        _payloadStore = new CombatReplayPayloadStore(replayRootPath);
        _battleCatalog = new PvpBattleCatalog(databasePath);
        EnsureSchema();
    }

    public IReadOnlyList<string> GetPendingCompletedRunIds(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT s.run_id
            FROM {RunLogSqliteSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            WHERE s.dirty = 1
              AND r.completed = 1
              AND r.game_mode = 'Ranked'
            ORDER BY COALESCE(s.last_attempt_at_utc, r.started_at_utc) ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var runIds = new List<string>();
        while (reader.Read())
            runIds.Add(reader.GetString(0));

        return runIds;
    }

    public bool HasMorePendingCompletedRuns()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT 1
            FROM {RunLogSqliteSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            WHERE s.dirty = 1
              AND r.completed = 1
              AND r.game_mode = 'Ranked'
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public void MarkRunUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunSyncStateTableName}
            SET last_attempt_at_utc = $attemptedAtUtc,
                retry_count = retry_count + 1,
                last_error = $error
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
    }

    public void MarkRunUploaded(
        string runId,
        long uploadedSeq,
        string? uploadedStatus,
        IReadOnlyList<string> battleIds,
        DateTimeOffset uploadedAtUtc
    )
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = 2;
            command.CommandText = $"""
                UPDATE {RunLogSqliteSchema.RunSyncStateTableName}
                SET dirty = 0,
                    uploaded_seq = $uploadedSeq,
                    uploaded_status = $uploadedStatus,
                    last_attempt_at_utc = $uploadedAtUtc,
                    last_uploaded_at_utc = $uploadedAtUtc,
                    retry_count = 0,
                    last_error = NULL
                WHERE run_id = $runId;
                """;
            command.Parameters.AddWithValue("$runId", runId);
            command.Parameters.AddWithValue("$uploadedSeq", uploadedSeq);
            command.Parameters.AddWithValue(
                "$uploadedStatus",
                uploadedStatus ?? (object)DBNull.Value
            );
            command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
            command.ExecuteNonQuery();
        }

        if (battleIds.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 2;
            var placeholders = new List<string>();
            for (var index = 0; index < battleIds.Count; index++)
            {
                var parameterName = $"$battleId{index}";
                placeholders.Add(parameterName);
                command.Parameters.AddWithValue(parameterName, battleIds[index]);
            }

            command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
            command.CommandText = $"""
                UPDATE {RunLogSqliteSchema.BattlesTableName}
                SET replay_dirty = 0,
                    replay_last_attempt_at_utc = $uploadedAtUtc,
                    replay_last_uploaded_at_utc = $uploadedAtUtc,
                    replay_retry_count = 0,
                    replay_last_error = NULL
                WHERE source = 'LOCAL'
                  AND battle_id IN ({string.Join(", ", placeholders)});
                """;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public RunBundleUploadSnapshot? TryBuildRunBundleSnapshot(
        string runId,
        string playerAccountId
    )
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                run_id,
                started_at_utc,
                status,
                hero,
                player_rank,
                player_rating,
                ended_at_utc,
                final_day,
                victories,
                losses,
                final_player_rank,
                final_player_rating,
                last_seq
            FROM {RunLogSqliteSchema.RunsTableName}
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var lastSeq = GetNullableInt64(reader, "last_seq") ?? 0L;
        var uploadedStatus = GetNullableString(reader, "status");
        var battleManifests = _battleCatalog.ListByRunId(runId);
        var battleProjections = new List<BattleProjectionV3>();
        var artifactBattles = new List<RunArtifactBattleV3>();
        var battleIds = new List<string>();

        foreach (var manifest in battleManifests)
        {
            if (string.IsNullOrWhiteSpace(manifest.BattleId))
                continue;

            var payload = _payloadStore.Load(manifest.BattleId);
            if (payload == null)
                return null;

            battleIds.Add(manifest.BattleId);
            battleProjections.Add(BuildBattleProjection(manifest));
            artifactBattles.Add(BuildArtifactBattle(manifest, payload));
        }

        var artifact = new RunArtifactV3 { RunId = runId, Battles = artifactBattles };
        var artifactBytes = V3RunBundleArtifactCodec.Serialize(artifact);

        return new RunBundleUploadSnapshot
        {
            RunId = runId,
            LastSeq = lastSeq,
            UploadedStatus = uploadedStatus,
            BattleIds = battleIds,
            Payload = new RunBundleUploadRequestV3
            {
                SchemaVersion = RunLogSqliteSchema.UploadPayloadSchemaVersion,
                PlayerAccountId = playerAccountId,
                SubmittedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                ArtifactCodec = V3RunBundleArtifactCodec.ContentType,
                ArtifactBytes = artifactBytes.ToArray(),
                RunProjection = new RunProjectionV3
                {
                    RunId = runId,
                    Status = uploadedStatus ?? string.Empty,
                    HeroId = null,
                    HeroName = GetNullableString(reader, "hero"),
                    PlayerRank = GetNullableString(reader, "player_rank"),
                    PlayerRating = GetNullableInt32(reader, "player_rating"),
                    PlayerPosition = null,
                    StartedAtUtc = GetNullableString(reader, "started_at_utc"),
                    EndedAtUtc = GetNullableString(reader, "ended_at_utc") ?? string.Empty,
                    FinalDay = GetNullableInt32(reader, "final_day"),
                    FinalWins = GetNullableInt32(reader, "victories"),
                    FinalLosses = GetNullableInt32(reader, "losses"),
                    FinalPlayerRank = GetNullableString(reader, "final_player_rank"),
                    FinalPlayerRating = GetNullableInt32(reader, "final_player_rating"),
                    FinalPlayerPosition = null,
                },
                BattleProjections = battleProjections,
            },
        };
    }

    private static BattleProjectionV3 BuildBattleProjection(PvpBattleManifest manifest)
    {
        return new BattleProjectionV3
        {
            BattleId = manifest.BattleId,
            RunId = manifest.RunId,
            RecordedAtUtc = manifest.RecordedAtUtc.ToString("o"),
            Day = manifest.Day,
            PlayerName = manifest.Participants.PlayerName,
            PlayerAccountId = manifest.Participants.PlayerAccountId,
            PlayerHero = manifest.Participants.PlayerHero,
            PlayerRank = manifest.Participants.PlayerRank,
            PlayerRating = manifest.Participants.PlayerRating,
            PlayerLevel = manifest.Participants.PlayerLevel,
            OpponentName = manifest.Participants.OpponentName,
            OpponentAccountId = manifest.Participants.OpponentAccountId,
            OpponentHero = manifest.Participants.OpponentHero,
            OpponentRank = manifest.Participants.OpponentRank,
            OpponentRating = manifest.Participants.OpponentRating,
            OpponentLevel = manifest.Participants.OpponentLevel,
            Result = manifest.Outcome.Result,
            ReplayAvailable = true,
        };
    }

    private static RunArtifactBattleV3 BuildArtifactBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload
    )
    {
        return new RunArtifactBattleV3
        {
            BattleId = manifest.BattleId,
            Manifest = new BattleManifestArtifactV3
            {
                BattleId = manifest.BattleId,
                RecordedAtUtc = manifest.RecordedAtUtc.ToString("o"),
                Day = manifest.Day,
                Hour = manifest.Hour,
                EncounterId = manifest.EncounterId,
                CombatKind = manifest.CombatKind,
                Result = manifest.Outcome.Result,
                WinnerCombatantId = manifest.Outcome.WinnerCombatantId,
                LoserCombatantId = manifest.Outcome.LoserCombatantId,
            },
            Participants = new BattleParticipantsArtifactV3
            {
                PlayerName = manifest.Participants.PlayerName,
                PlayerAccountId = manifest.Participants.PlayerAccountId,
                PlayerHero = manifest.Participants.PlayerHero,
                PlayerRank = manifest.Participants.PlayerRank,
                PlayerRating = manifest.Participants.PlayerRating,
                PlayerLevel = manifest.Participants.PlayerLevel,
                OpponentName = manifest.Participants.OpponentName,
                OpponentAccountId = manifest.Participants.OpponentAccountId,
                OpponentHero = manifest.Participants.OpponentHero,
                OpponentRank = manifest.Participants.OpponentRank,
                OpponentRating = manifest.Participants.OpponentRating,
                OpponentLevel = manifest.Participants.OpponentLevel,
            },
            Snapshots = new BattleSnapshotsArtifactV3
            {
                CardSets = new List<CardSetCaptureArtifactV3>
                {
                    CreateCardSet("player_hand", manifest.Snapshots.PlayerHand),
                    CreateCardSet("player_skills", manifest.Snapshots.PlayerSkills),
                    CreateCardSet("opponent_hand", manifest.Snapshots.OpponentHand),
                    CreateCardSet("opponent_skills", manifest.Snapshots.OpponentSkills),
                },
            },
            ReplayPayload = new ReplayPayloadArtifactV3
            {
                BattleId = payload.BattleId,
                Version = payload.Version,
                SpawnMessageBytes = payload.SpawnMessageBytes?.ToArray() ?? [],
                CombatMessageBytes = payload.CombatMessageBytes?.ToArray() ?? [],
                DespawnMessageBytes = payload.DespawnMessageBytes?.ToArray() ?? [],
            },
        };
    }

    private static CardSetCaptureArtifactV3 CreateCardSet(
        string label,
        PvpBattleCardSetCapture capture
    )
    {
        return new CardSetCaptureArtifactV3
        {
            Label = label,
            Status = capture.Status.ToString(),
            Source = capture.Source.ToString(),
            Items =
                capture.Items?.ToList()
                ?? new List<BazaarPlusPlus.Game.CombatReplay.CombatReplayCardSnapshot>(),
        };
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        RunLogSqliteSchema.EnsureInitialized(connection);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 2000;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? GetNullableInt64(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }
}
