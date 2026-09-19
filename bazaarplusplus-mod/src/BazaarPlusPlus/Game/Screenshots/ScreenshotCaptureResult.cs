#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureResult
{
    public ScreenshotCaptureMetadata Metadata { get; init; }

    public string ScreenshotId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? HeroName { get; set; }

    public string? BattleId { get; set; }

    public RunScreenshotCaptureSource CaptureSource { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public DateTimeOffset CapturedAtLocal { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }
}

// Copy scalar values before frame acquisition releases Continue. Never retain the mutable
// probe DTOs or re-read the live run while the image is being encoded and persisted.
internal readonly record struct ScreenshotCaptureMetadata(
    int? Day,
    int? Victories,
    string? Hero,
    string? Rank,
    int? Rating,
    int? Position
)
{
    internal static ScreenshotCaptureMetadata Capture(IRunSnapshotProbe probe)
    {
        var basics = probe.TryGetRunBasics(out var run) ? run : null;
        var rank = probe.TryGetRankSnapshot(out var ranking) ? ranking : null;
        var position = probe.TryGetLeaderboardPosition(out var placement) ? placement : null;
        return new(
            basics?.Day,
            basics?.Victories,
            basics?.Hero,
            rank?.Rank,
            rank?.Rating,
            position
        );
    }
}
