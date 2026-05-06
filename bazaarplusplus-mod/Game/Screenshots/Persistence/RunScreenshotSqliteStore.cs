#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

namespace BazaarPlusPlus.Game.Screenshots.Persistence;

internal sealed class RunScreenshotSqliteStore : SqlitePersistenceStoreBase
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
            INSERT INTO {RunLogSqliteSchema.RunScreenshotsTableName} (
                screenshot_id,
                run_id,
                hero_name,
                battle_id,
                capture_source,
                is_primary,
                image_relative_path,
                captured_at_local,
                captured_at_utc,
                day,
                player_rank,
                player_rating,
                player_position,
                victories_at_capture
            ) VALUES (
                $screenshotId,
                $runId,
                $heroName,
                $battleId,
                $captureSource,
                $isPrimary,
                $imageRelativePath,
                $capturedAtLocal,
                $capturedAtUtc,
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
        command.Parameters.AddWithValue("$battleId", (object?)record.BattleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$captureSource", GetStorageValue());
        command.Parameters.AddWithValue("$isPrimary", record.IsPrimary ? 1 : 0);
        command.Parameters.AddWithValue("$imageRelativePath", record.ImageRelativePath);
        command.Parameters.AddWithValue("$capturedAtLocal", record.CapturedAtLocal.ToString("o"));
        command.Parameters.AddWithValue("$capturedAtUtc", record.CapturedAtUtc.ToString("o"));
        AddNullableInt32(command, "$day", record.Day);
        command.Parameters.AddWithValue("$playerRank", (object?)record.PlayerRank ?? DBNull.Value);
        AddNullableInt32(command, "$playerRating", record.PlayerRating);
        AddNullableInt32(command, "$playerPosition", record.PlayerPosition);
        AddNullableInt32(command, "$victoriesAtCapture", record.VictoriesAtCapture);
        command.ExecuteNonQuery();
    }

    private static string GetStorageValue()
    {
        return "end_of_run_auto";
    }
}
