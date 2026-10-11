#nullable enable
namespace BazaarPlusPlus.Storage.RunScreenshot;

public sealed class RunScreenshotRecord
{
    public string ScreenshotId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? HeroName { get; set; }

    public bool IsPrimary { get; set; }

    public string ImageRelativePath { get; set; } = string.Empty;

    // The capture instant in the player's local offset; only its offset is stored.
    public DateTimeOffset CapturedAtLocal { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public int? Day { get; set; }

    public string? PlayerRank { get; set; }

    public int? PlayerRating { get; set; }

    public int? PlayerPosition { get; set; }

    public int? VictoriesAtCapture { get; set; }
}

public sealed class RunScreenshotArtifact
{
    public RunScreenshotArtifact(string imageRelativePath, DateTimeOffset capturedAtUtc)
    {
        ImageRelativePath = imageRelativePath;
        CapturedAtUtc = capturedAtUtc;
    }

    public string ImageRelativePath { get; }
    public DateTimeOffset CapturedAtUtc { get; }
}
