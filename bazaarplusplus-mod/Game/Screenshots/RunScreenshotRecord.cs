#nullable enable
using System;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class RunScreenshotRecord
{
    public string ScreenshotId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? HeroName { get; set; }

    public string? BattleId { get; set; }

    public RunScreenshotCaptureSource CaptureSource { get; set; }

    public bool IsPrimary { get; set; }

    public string ImageRelativePath { get; set; } = string.Empty;

    public DateTimeOffset CapturedAtLocal { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public int? Day { get; set; }

    public string? PlayerRank { get; set; }

    public int? PlayerRating { get; set; }

    public int? PlayerPosition { get; set; }

    public int? VictoriesAtCapture { get; set; }
}
