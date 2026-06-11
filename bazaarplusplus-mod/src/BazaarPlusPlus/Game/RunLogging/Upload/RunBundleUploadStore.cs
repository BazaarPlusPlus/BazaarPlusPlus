#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Models;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadStore : SqliteStoreBase
{
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly IPvpBattleCatalog _battleCatalog;

    public RunBundleUploadStore(
        string databasePath,
        string replayRootPath,
        IPvpBattleCatalog battleCatalog
    )
        : base(databasePath)
    {
        if (string.IsNullOrWhiteSpace(replayRootPath))
            throw new ArgumentException("Replay root path is required.", nameof(replayRootPath));

        _payloadStore = new CombatReplayPayloadStore(replayRootPath);
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
    }

    public IReadOnlyList<string> GetPendingCompletedRunIds(int limit)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT s.run_id
            FROM {RunLogSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            WHERE s.dirty = 1
              AND r.completed = 1
              AND r.game_mode = '{RunLogSchema.GameModeRanked}'
            ORDER BY s.retry_count ASC,
                     s.last_attempt_at_utc ASC,
                     s.run_id ASC
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
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT 1
            FROM {RunLogSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            WHERE s.dirty = 1
              AND r.completed = 1
              AND r.game_mode = '{RunLogSchema.GameModeRanked}'
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public void MarkRunUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.RunSyncStateTableName}
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

        using (var command = CreateCommand(connection, transaction))
        {
            command.CommandText = $"""
                UPDATE {RunLogSchema.RunSyncStateTableName}
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
            using var command = CreateCommand(connection, transaction);
            var placeholders = new List<string>();
            for (var index = 0; index < battleIds.Count; index++)
            {
                var parameterName = $"$battleId{index}";
                placeholders.Add(parameterName);
                command.Parameters.AddWithValue(parameterName, battleIds[index]);
            }

            command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
            command.CommandText = $"""
                UPDATE {RunLogSchema.BattlesTableName}
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

    public RunBundleUploadSnapshot? TryBuildRunBundleSnapshot(string runId, string playerAccountId)
    {
        var runRow = TryGetRunUploadRow(runId);
        if (runRow == null)
            return null;

        var battleManifests = _battleCatalog.ListByRunId(runId);
        var battleProjections = new List<BattleProjection>();
        var artifactBattles = new List<RunArtifactBattle>();
        var battleIds = new List<string>();
        var runEnded = !string.IsNullOrWhiteSpace(runRow.EndedAtUtc);
        var finalBattleId = runEnded
            ? battleManifests
                .Where(manifest => !string.IsNullOrWhiteSpace(manifest.BattleId))
                .Select(manifest => manifest.BattleId)
                .LastOrDefault()
            : null;

        foreach (var manifest in battleManifests)
        {
            if (string.IsNullOrWhiteSpace(manifest.BattleId))
                continue;

            var payload = _payloadStore.Load(manifest.BattleId);
            if (payload == null)
                return null;

            battleIds.Add(manifest.BattleId);
            battleProjections.Add(
                BuildBattleProjection(
                    manifest,
                    runEnded
                        && string.Equals(manifest.BattleId, finalBattleId, StringComparison.Ordinal)
                )
            );
            artifactBattles.Add(BuildArtifactBattle(manifest, payload));
        }

        var artifact = new RunArtifact { RunId = runId, Battles = artifactBattles };
        var artifactBytes = RunBundleArtifactCodec.Serialize(artifact);

        return new RunBundleUploadSnapshot
        {
            RunId = runId,
            LastSeq = runRow.LastSeq,
            UploadedStatus = runRow.Status,
            BattleIds = battleIds,
            ArtifactBytes = artifactBytes,
            Metadata = new RunBundleUploadRequest
            {
                SchemaVersion = RunLogSchema.UploadPayloadSchemaVersion,
                PlayerAccountId = playerAccountId,
                SubmittedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                ArtifactCodec = RunBundleArtifactCodec.ContentType,
                RunProjection = new RunProjection
                {
                    RunId = runId,
                    Status = runRow.Status ?? string.Empty,
                    HeroId = null,
                    HeroName = runRow.Hero,
                    PlayerRank = runRow.PlayerRank,
                    PlayerRating = runRow.PlayerRating,
                    PlayerPosition = null,
                    StartedAtUtc = runRow.StartedAtUtc,
                    EndedAtUtc = runRow.EndedAtUtc ?? string.Empty,
                    FinalDay = runRow.FinalDay,
                    FinalWins = runRow.Victories,
                    FinalLosses = runRow.Losses,
                    FinalPlayerRank = runRow.FinalPlayerRank,
                    FinalPlayerRating = runRow.FinalPlayerRating,
                    FinalPlayerPosition = null,
                },
                BattleProjections = battleProjections,
            },
        };
    }

    private RunUploadRow? TryGetRunUploadRow(string runId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
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
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new RunUploadRow
        {
            RunId = runId,
            StartedAtUtc = GetNullableString(reader, "started_at_utc"),
            Status = GetNullableString(reader, "status"),
            Hero = GetNullableString(reader, "hero"),
            PlayerRank = GetNullableString(reader, "player_rank"),
            PlayerRating = GetNullableInt32(reader, "player_rating"),
            EndedAtUtc = GetNullableString(reader, "ended_at_utc"),
            FinalDay = GetNullableInt32(reader, "final_day"),
            Victories = GetNullableInt32(reader, "victories"),
            Losses = GetNullableInt32(reader, "losses"),
            FinalPlayerRank = GetNullableString(reader, "final_player_rank"),
            FinalPlayerRating = GetNullableInt32(reader, "final_player_rating"),
            LastSeq = GetNullableInt64(reader, "last_seq") ?? 0L,
        };
    }

    private sealed class RunUploadRow
    {
        public string RunId { get; set; } = string.Empty;

        public string? StartedAtUtc { get; set; }

        public string? Status { get; set; }

        public string? Hero { get; set; }

        public string? PlayerRank { get; set; }

        public int? PlayerRating { get; set; }

        public string? EndedAtUtc { get; set; }

        public int? FinalDay { get; set; }

        public int? Victories { get; set; }

        public int? Losses { get; set; }

        public string? FinalPlayerRank { get; set; }

        public int? FinalPlayerRating { get; set; }

        public long LastSeq { get; set; }
    }

    private static BattleProjection BuildBattleProjection(
        PvpBattleManifest manifest,
        bool isFinalBattle
    )
    {
        return new BattleProjection
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
            PlayerPrestige = manifest.Participants.PlayerPrestige,
            PlayerVictories = manifest.Participants.PlayerVictories,
            OpponentName = manifest.Participants.OpponentName,
            OpponentAccountId = manifest.Participants.OpponentAccountId,
            OpponentHero = manifest.Participants.OpponentHero,
            OpponentRank = manifest.Participants.OpponentRank,
            OpponentRating = manifest.Participants.OpponentRating,
            OpponentLevel = manifest.Participants.OpponentLevel,
            OpponentPrestige = manifest.Participants.OpponentPrestige,
            OpponentVictories = manifest.Participants.OpponentVictories,
            Result = manifest.Outcome.Result,
            WinnerCombatantId = manifest.Outcome.WinnerCombatantId,
            LoserCombatantId = manifest.Outcome.LoserCombatantId,
            IsFinalBattle = isFinalBattle,
        };
    }

    private static RunArtifactBattle BuildArtifactBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload
    )
    {
        return new RunArtifactBattle
        {
            BattleId = manifest.BattleId,
            Manifest = new BattleManifestArtifact
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
            Participants = new BattleParticipantsArtifact
            {
                PlayerName = manifest.Participants.PlayerName,
                PlayerAccountId = manifest.Participants.PlayerAccountId,
                PlayerHero = manifest.Participants.PlayerHero,
                PlayerRank = manifest.Participants.PlayerRank,
                PlayerRating = manifest.Participants.PlayerRating,
                PlayerLevel = manifest.Participants.PlayerLevel,
                PlayerPrestige = manifest.Participants.PlayerPrestige,
                PlayerVictories = manifest.Participants.PlayerVictories,
                OpponentName = manifest.Participants.OpponentName,
                OpponentAccountId = manifest.Participants.OpponentAccountId,
                OpponentHero = manifest.Participants.OpponentHero,
                OpponentRank = manifest.Participants.OpponentRank,
                OpponentRating = manifest.Participants.OpponentRating,
                OpponentLevel = manifest.Participants.OpponentLevel,
                OpponentPrestige = manifest.Participants.OpponentPrestige,
                OpponentVictories = manifest.Participants.OpponentVictories,
            },
            Snapshots = new BattleSnapshotsArtifact
            {
                CardSets = new List<CardSetCaptureArtifact>
                {
                    CreateCardSet("player_hand", manifest.Snapshots.PlayerHand),
                    CreateCardSet("player_skills", manifest.Snapshots.PlayerSkills),
                    CreateCardSet("opponent_hand", manifest.Snapshots.OpponentHand),
                    CreateCardSet("opponent_skills", manifest.Snapshots.OpponentSkills),
                },
            },
            ReplayPayload = new ReplayPayloadArtifact
            {
                BattleId = payload.BattleId,
                Version = payload.Version,
                SpawnMessageBytes = payload.SpawnMessageBytes?.ToArray() ?? [],
                CombatMessageBytes = payload.CombatMessageBytes?.ToArray() ?? [],
                DespawnMessageBytes = payload.DespawnMessageBytes?.ToArray() ?? [],
            },
        };
    }

    private static CardSetCaptureArtifact CreateCardSet(
        string label,
        PvpBattleCardSetCapture capture
    )
    {
        return new CardSetCaptureArtifact
        {
            Label = label,
            Status = capture.Status.ToString(),
            Source = capture.Source.ToString(),
            Items =
                capture.Items?.Select(MapCardSnapshot).ToList() ?? new List<CardSetItemArtifact>(),
        };
    }

    private static CardSetItemArtifact MapCardSnapshot(PvpBattleCardSnapshot snapshot)
    {
        return new CardSetItemArtifact
        {
            InstanceId = snapshot.InstanceId,
            TemplateId = snapshot.TemplateId,
            Type = (int)snapshot.Type,
            Size = (int)snapshot.Size,
            Section = snapshot.Section.HasValue ? (int?)snapshot.Section.Value : null,
            Socket = snapshot.Socket.HasValue ? (int?)snapshot.Socket.Value : null,
            Name = snapshot.Name,
            Tier = snapshot.Tier,
            Enchant = snapshot.Enchant,
            Tags = new List<string>(snapshot.Tags ?? new List<string>()),
            Attributes = new Dictionary<string, int>(
                snapshot.Attributes ?? new Dictionary<string, int>()
            ),
        };
    }
}
