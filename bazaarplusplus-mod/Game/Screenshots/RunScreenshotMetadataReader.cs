#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.RunLogging;
using TheBazaar;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class RunScreenshotMetadataReader
{
    public static RunScreenshotRecord CreateRecord(
        ScreenshotCaptureResult capture,
        bool isPrimary = false
    )
    {
        RunLoggingGameDataReader.TryGetPlayerRankSnapshot(out var playerRank, out var playerRating);
        BppClientCacheBridge.TryGetPlayerLeaderboardPosition(out var playerPosition);
        var heroName = !string.IsNullOrWhiteSpace(capture.HeroName)
            ? capture.HeroName
            : Data.Run?.Player?.Hero.ToString();

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
            Day = Data.Run == null ? null : (int?)Data.Run.Day,
            PlayerRank = playerRank,
            PlayerRating = playerRating,
            PlayerPosition = playerPosition,
            VictoriesAtCapture = Data.Run == null ? null : unchecked((int)Data.Run.Victories),
        };
    }
}
