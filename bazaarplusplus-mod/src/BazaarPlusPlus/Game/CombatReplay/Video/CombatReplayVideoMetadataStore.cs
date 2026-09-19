#nullable enable
using System.Globalization;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal interface IReplayVideoArtifactCatalog
{
    IReadOnlyList<ReplayVideoArtifactRecord> ListArtifacts();
    IReadOnlyList<ReplayVideoArtifactRecord> ListDetachedArtifacts();
    void ReconcileFileState(
        IReadOnlyCollection<ReplayVideoArtifactRecord> observations,
        ReplayVideoFileState fileState,
        DateTimeOffset reconciledAt
    );
    void MarkDetachedArtifactDeleted(string videoId, DateTimeOffset deletedAt);
}

internal sealed class CombatReplayVideoMetadataStore : SqliteStoreBase, IReplayVideoArtifactCatalog
{
    public CombatReplayVideoMetadataStore(string databasePath)
        : base(databasePath) { }

    public void SaveStart(VideoRecordingStarted record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.CombatReplayVideosTableName} (
                video_id,
                battle_id,
                source,
                video_relative_path,
                width,
                height,
                fps,
                codec,
                crf,
                preset,
                started_at_utc,
                captured_frames,
                dropped_frames,
                status,
                attachment_state,
                file_state,
                detached_at_utc
            ) VALUES (
                $videoId,
                $battleId,
                $source,
                $videoRelativePath,
                $width,
                $height,
                $fps,
                $codec,
                $crf,
                $preset,
                $startedAtUtc,
                0,
                0,
                'RECORDING',
                CASE WHEN EXISTS (
                    SELECT 1 FROM {RunLogSchema.BattlesTableName} WHERE battle_id = $battleId
                ) THEN 'attached' ELSE 'detached' END,
                'pending',
                CASE WHEN EXISTS (
                    SELECT 1 FROM {RunLogSchema.BattlesTableName} WHERE battle_id = $battleId
                ) THEN NULL ELSE $startedAtUtc END
            );
            """;
        command.Parameters.AddWithValue("$videoId", record.VideoId);
        command.Parameters.AddWithValue("$battleId", record.BattleId);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$videoRelativePath", record.VideoRelativePath);
        command.Parameters.AddWithValue("$width", record.Width);
        command.Parameters.AddWithValue("$height", record.Height);
        command.Parameters.AddWithValue("$fps", record.Fps);
        command.Parameters.AddWithValue("$codec", record.Codec);
        AddNullableInt32(command, "$crf", record.Crf);
        AddNullableString(command, "$preset", record.Preset);
        command.Parameters.AddWithValue("$startedAtUtc", record.StartedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void SaveFinish(VideoRecordingFinished record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.CombatReplayVideosTableName}
            SET
                video_relative_path = $videoRelativePath,
                ended_at_utc = $endedAtUtc,
                duration_ms = $durationMs,
                captured_frames = $capturedFrames,
                dropped_frames = $droppedFrames,
                file_size_bytes = $fileSizeBytes,
                status = $status,
                error = $error,
                file_state = CASE
                    WHEN upper($status) = 'COMPLETED' AND COALESCE($fileSizeBytes, 0) > 0
                        THEN 'present'
                    ELSE 'missing'
                END,
                missing_at_utc = CASE
                    WHEN upper($status) = 'COMPLETED' AND COALESCE($fileSizeBytes, 0) > 0
                        THEN NULL
                    ELSE $endedAtUtc
                END,
                last_reconciled_at_utc = $endedAtUtc
            WHERE video_id = $videoId;
            """;
        command.Parameters.AddWithValue("$videoId", record.VideoId);
        command.Parameters.AddWithValue("$videoRelativePath", record.VideoRelativePath);
        command.Parameters.AddWithValue("$endedAtUtc", record.EndedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$durationMs", record.DurationMs);
        command.Parameters.AddWithValue("$capturedFrames", record.CapturedFrames);
        command.Parameters.AddWithValue("$droppedFrames", record.DroppedFrames);
        AddNullableInt64(command, "$fileSizeBytes", record.FileSizeBytes);
        command.Parameters.AddWithValue("$status", record.Status);
        AddNullableString(command, "$error", record.Error);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ReplayVideoArtifactRecord> ListArtifacts()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT video_id, battle_id, video_relative_path, status,
                   attachment_state, file_state, started_at_utc, ended_at_utc,
                   detached_at_utc, missing_at_utc, last_reconciled_at_utc
            FROM {RunLogSchema.CombatReplayVideosTableName}
            ORDER BY started_at_utc DESC, video_id DESC;
            """;
        using var reader = command.ExecuteReader();
        var records = new List<ReplayVideoArtifactRecord>();
        while (reader.Read())
            records.Add(ReadArtifact(reader));
        return records;
    }

    public IReadOnlyList<ReplayVideoArtifactRecord> ListDetachedArtifacts()
    {
        return ListArtifacts()
            .Where(record =>
                record.AttachmentState == ReplayVideoAttachmentState.Detached
                && record.FileState != ReplayVideoFileState.Deleted
            )
            .ToList();
    }

    public void ReconcileFileState(
        IReadOnlyCollection<ReplayVideoArtifactRecord> observations,
        ReplayVideoFileState fileState,
        DateTimeOffset reconciledAt
    )
    {
        var normalized = observations
            .Where(observation => !string.IsNullOrWhiteSpace(observation.VideoId))
            .GroupBy(observation => observation.VideoId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (normalized.Count == 0)
            return;

        var observationsJson = Newtonsoft.Json.JsonConvert.SerializeObject(
            normalized.Select(observation =>
                new object[]
                {
                    observation.VideoId,
                    ToStorage(observation.FileState),
                    observation.Status,
                    observation.EndedAtUtc?.ToString("o") ?? string.Empty,
                }
            )
        );

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.CombatReplayVideosTableName}
            SET file_state = $fileState,
                missing_at_utc = CASE
                    WHEN $fileState = 'missing' THEN COALESCE(missing_at_utc, $now)
                    ELSE NULL
                END,
                last_reconciled_at_utc = $now
            WHERE file_state <> 'deleted'
              AND EXISTS (
                  SELECT 1
                  FROM json_each($observationsJson) AS observation
                  WHERE json_extract(observation.value, '$[0]') = video_id
                    AND json_extract(observation.value, '$[1]') = file_state
                    AND json_extract(observation.value, '$[2]') = status
                    AND json_extract(observation.value, '$[3]') = COALESCE(ended_at_utc, '')
              );
            """;
        command.Parameters.AddWithValue("$fileState", ToStorage(fileState));
        command.Parameters.AddWithValue("$now", reconciledAt.ToString("o"));
        command.Parameters.AddWithValue("$observationsJson", observationsJson);
        command.ExecuteNonQuery();
    }

    public void MarkDetachedArtifactDeleted(string videoId, DateTimeOffset deletedAt)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return;

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.CombatReplayVideosTableName}
            SET file_state = 'deleted',
                missing_at_utc = NULL,
                last_reconciled_at_utc = $now
            WHERE video_id = $videoId
              AND attachment_state = 'detached';
            """;
        command.Parameters.AddWithValue("$videoId", videoId);
        command.Parameters.AddWithValue("$now", deletedAt.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static ReplayVideoArtifactRecord ReadArtifact(
        Microsoft.Data.Sqlite.SqliteDataReader reader
    ) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ParseAttachmentState(reader.GetString(4)),
            ParseFileState(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            ReadDateTimeOffset(reader, 7),
            ReadDateTimeOffset(reader, 8),
            ReadDateTimeOffset(reader, 9),
            ReadDateTimeOffset(reader, 10)
        );

    private static DateTimeOffset? ReadDateTimeOffset(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal
    ) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static List<string> NormalizeIds(IReadOnlyCollection<string>? ids) =>
        ids == null
            ? []
            : ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

    private static string ToStorage(ReplayVideoFileState state) =>
        state switch
        {
            ReplayVideoFileState.Pending => "pending",
            ReplayVideoFileState.Present => "present",
            ReplayVideoFileState.Missing => "missing",
            ReplayVideoFileState.Deleted => "deleted",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static ReplayVideoFileState ParseFileState(string state) =>
        state switch
        {
            "pending" => ReplayVideoFileState.Pending,
            "present" => ReplayVideoFileState.Present,
            "missing" => ReplayVideoFileState.Missing,
            "deleted" => ReplayVideoFileState.Deleted,
            _ => throw new InvalidOperationException($"Unknown replay video file state '{state}'."),
        };

    private static ReplayVideoAttachmentState ParseAttachmentState(string state) =>
        state switch
        {
            "attached" => ReplayVideoAttachmentState.Attached,
            "detached" => ReplayVideoAttachmentState.Detached,
            _ => throw new InvalidOperationException(
                $"Unknown replay video attachment state '{state}'."
            ),
        };
}

internal enum ReplayVideoAttachmentState
{
    Attached,
    Detached,
}

internal enum ReplayVideoFileState
{
    Pending,
    Present,
    Missing,
    Deleted,
}

internal readonly record struct ReplayVideoArtifactRecord(
    string VideoId,
    string BattleId,
    string VideoRelativePath,
    string Status,
    ReplayVideoAttachmentState AttachmentState,
    ReplayVideoFileState FileState,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    DateTimeOffset? DetachedAtUtc,
    DateTimeOffset? MissingAtUtc,
    DateTimeOffset? LastReconciledAtUtc
);

internal sealed class VideoRecordingStarted
{
    public string VideoId { get; init; } = string.Empty;
    public string BattleId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string VideoRelativePath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int Fps { get; init; }
    public string Codec { get; init; } = "libx264";
    public int? Crf { get; init; }
    public string? Preset { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
}

internal sealed class VideoRecordingFinished
{
    public string VideoId { get; init; } = string.Empty;
    public string VideoRelativePath { get; init; } = string.Empty;
    public DateTimeOffset EndedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int CapturedFrames { get; init; }
    public int DroppedFrames { get; init; }
    public long? FileSizeBytes { get; init; }
    public string Status { get; init; } = "COMPLETED";
    public string? Error { get; init; }
}
