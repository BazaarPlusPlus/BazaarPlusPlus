#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class RunScreenshotRecordMapper
{
    public static RunScreenshotRecord CreateRecord(
        ScreenshotCaptureResult capture,
        RunBasicsSnapshot? basics,
        RankSnapshot? rank,
        int? position,
        bool isPrimary,
        string? buildChannel
    )
    {
        var heroName = !string.IsNullOrWhiteSpace(capture.HeroName)
            ? capture.HeroName
            : basics?.Hero;

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
            Day = basics?.Day,
            PlayerRank = rank?.Rank,
            PlayerRating = rank?.Rating,
            PlayerPosition = position,
            VictoriesAtCapture = basics?.Victories,
            BuildChannel = buildChannel,
        };
    }
}
