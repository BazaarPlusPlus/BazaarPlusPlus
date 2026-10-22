#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

public sealed class ScreenshotCaptureTerminal
{
    public string? RunId { get; set; }
    public ScreenshotArtifactStatus ArtifactStatus { get; set; }
    public bool MetadataPersisted { get; set; }
}
