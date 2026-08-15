#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureTerminal
{
    public string? RunId { get; set; }
    public ScreenshotArtifactStatus ArtifactStatus { get; set; }
    public bool MetadataPersisted { get; set; }
}
