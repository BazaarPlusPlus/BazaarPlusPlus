#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

public sealed class ScreenshotCaptureRequest
{
    public string ScreenshotId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? HeroName { get; set; }
}
