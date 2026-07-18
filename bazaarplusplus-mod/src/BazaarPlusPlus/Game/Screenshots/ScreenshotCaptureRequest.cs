#nullable enable
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureRequest
{
    public string ScreenshotId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? HeroName { get; set; }

    public string? BattleId { get; set; }

    public RunScreenshotCaptureSource CaptureSource { get; set; }
}
