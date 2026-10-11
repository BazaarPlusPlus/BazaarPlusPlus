#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Storage.RunScreenshot;

public sealed class RunScreenshotSqliteStore : SqliteStoreBase
{
    public RunScreenshotSqliteStore(string databasePath)
        : base(databasePath) { }

    public void Save(RunScreenshotRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(record.ScreenshotId))
            throw new ArgumentException("Screenshot id is required.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.ImageRelativePath))
            throw new ArgumentException("Image path is required.", nameof(record));

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.RunScreenshotsTableName} (
                screenshot_id,
                run_id,
                hero_name,
                capture_source,
                is_primary,
                image_relative_path,
                captured_at_ms,
                captured_utc_offset_minutes,
                day,
                player_rank,
                player_rating,
                player_position,
                victories_at_capture
            ) VALUES (
                $screenshotId,
                $runId,
                $heroName,
                $captureSource,
                $isPrimary,
                $imageRelativePath,
                $capturedAtMs,
                $capturedUtcOffsetMinutes,
                $day,
                $playerRank,
                $playerRating,
                $playerPosition,
                $victoriesAtCapture
            );
            """;
        command.Parameters.AddWithValue("$screenshotId", record.ScreenshotId);
        command.Parameters.AddWithValue("$runId", (object?)record.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$heroName", (object?)record.HeroName ?? DBNull.Value);
        command.Parameters.AddWithValue("$captureSource", RunLogSchema.CaptureSourceEndOfRunAuto);
        command.Parameters.AddWithValue("$isPrimary", record.IsPrimary ? 1 : 0);
        command.Parameters.AddWithValue("$imageRelativePath", record.ImageRelativePath);
        command.Parameters.AddWithValue(
            "$capturedAtMs",
            record.CapturedAtUtc.ToUnixTimeMilliseconds()
        );
        command.Parameters.AddWithValue(
            "$capturedUtcOffsetMinutes",
            (int)record.CapturedAtLocal.Offset.TotalMinutes
        );
        AddNullableInt32(command, "$day", record.Day);
        command.Parameters.AddWithValue("$playerRank", (object?)record.PlayerRank ?? DBNull.Value);
        AddNullableInt32(command, "$playerRating", record.PlayerRating);
        AddNullableInt32(command, "$playerPosition", record.PlayerPosition);
        AddNullableInt32(command, "$victoriesAtCapture", record.VictoriesAtCapture);
        command.ExecuteNonQuery();
    }

    public RunScreenshotArtifact? TryGetLatestPrimaryForRun(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run id is required.", nameof(runId));
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT image_relative_path, captured_at_ms
            FROM {RunLogSchema.RunScreenshotsTableName}
            WHERE run_id = $runId AND is_primary = 1
            ORDER BY captured_at_ms DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new RunScreenshotArtifact(
                reader.GetString(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1))
            )
            : null;
    }
}
