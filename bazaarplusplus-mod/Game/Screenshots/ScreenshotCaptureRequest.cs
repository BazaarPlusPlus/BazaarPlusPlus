#nullable enable

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureRequest
{
    public string? RunId { get; set; }

    public string? HeroName { get; set; }

    public string? BattleId { get; set; }

    public RunScreenshotCaptureSource CaptureSource { get; set; }
}
