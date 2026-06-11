#nullable enable
using System;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class CombatReplayVideoMetadataStore : SqliteStoreBase
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
                status
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
                'RECORDING'
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
                error = $error
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
}

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
