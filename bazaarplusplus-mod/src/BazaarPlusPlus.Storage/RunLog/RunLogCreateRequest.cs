#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogCreateRequest
{
    public string RunId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public string Hero { get; set; } = string.Empty;

    public string GameMode { get; set; } = string.Empty;

    public string? PlayerRank { get; set; }

    public int? PlayerRating { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string Status { get; set; } = "active";

    // A Run recorded on PTR remains ineligible for upload after switching channels.
    public string? BuildChannel { get; set; }

    public string? PlayerAccountId { get; set; }

    public bool BundleScreenshotRequested { get; set; }

    public string? ModVersion { get; set; }
}
