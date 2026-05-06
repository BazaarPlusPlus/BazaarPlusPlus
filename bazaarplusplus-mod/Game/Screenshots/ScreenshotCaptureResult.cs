#nullable enable
using System;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureResult
{
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
