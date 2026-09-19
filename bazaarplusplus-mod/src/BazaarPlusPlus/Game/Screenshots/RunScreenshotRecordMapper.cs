#nullable enable
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class RunScreenshotRecordMapper
{
    public static RunScreenshotRecord CreateRecord(
        ScreenshotCaptureResult capture,
        bool isPrimary,
        string? buildChannel
    )
    {
        var metadata = capture.Metadata;
        var heroName = !string.IsNullOrWhiteSpace(capture.HeroName)
            ? capture.HeroName
            : metadata.Hero;

        return new RunScreenshotRecord
        {
            ScreenshotId = capture.ScreenshotId,
            RunId = capture.RunId,
            HeroName = heroName,
            BattleId = capture.BattleId,
            CaptureSource = capture.CaptureSource,
            IsPrimary = isPrimary,
            ImageRelativePath = capture.RelativePath,
            CapturedAtLocal = capture.CapturedAtLocal,
            CapturedAtUtc = capture.CapturedAtUtc,
            Day = metadata.Day,
            PlayerRank = metadata.Rank,
            PlayerRating = metadata.Rating,
            PlayerPosition = metadata.Position,
            VictoriesAtCapture = metadata.Victories,
            BuildChannel = buildChannel,
        };
    }
}
