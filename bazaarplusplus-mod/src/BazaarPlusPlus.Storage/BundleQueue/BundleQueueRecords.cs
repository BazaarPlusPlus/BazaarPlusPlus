#nullable enable
namespace BazaarPlusPlus.Storage.BundleQueue;

public enum BundleSealJobState
{
    Waiting,
    Sealing,
    TerminalFailure,
}

public enum BundleScreenshotState
{
    NotRequested,
    Waiting,
    Available,
    Unavailable,
    TimedOut,
}

public sealed class BundleSealJobRecord
{
    public BundleSealJobRecord(
        string runId,
        BundleSealJobState state,
        string? playerAccountId,
        bool screenshotRequested,
        BundleScreenshotState screenshotState,
        DateTimeOffset inputDeadlineAtUtc,
        string? bundleId,
        long? createdAtMs
    )
    {
        RunId = runId;
        State = state;
        PlayerAccountId = playerAccountId;
        ScreenshotRequested = screenshotRequested;
        ScreenshotState = screenshotState;
        InputDeadlineAtUtc = inputDeadlineAtUtc;
        BundleId = bundleId;
        CreatedAtMs = createdAtMs;
    }

    public string RunId { get; }
    public BundleSealJobState State { get; }
    public string? PlayerAccountId { get; }
    public bool ScreenshotRequested { get; }
    public BundleScreenshotState ScreenshotState { get; }
    public DateTimeOffset InputDeadlineAtUtc { get; }
    public string? BundleId { get; }
    public long? CreatedAtMs { get; }
}

public sealed class BundleAllocationRecord
{
    public BundleAllocationRecord(string bundleId, long createdAtMs)
    {
        BundleId = bundleId;
        CreatedAtMs = createdAtMs;
    }

    public string BundleId { get; }
    public long CreatedAtMs { get; }
}

public sealed class BundleOutboxPublishRecord
{
    public BundleOutboxPublishRecord(
        string bundleId,
        string runId,
        string fileName,
        string contentSha256Hex,
        string contentDigest,
        long totalBytes,
        bool hasScreenshot
    )
    {
        BundleId = bundleId;
        RunId = runId;
        FileName = fileName;
        ContentSha256Hex = contentSha256Hex;
        ContentDigest = contentDigest;
        TotalBytes = totalBytes;
        HasScreenshot = hasScreenshot;
    }

    public string BundleId { get; }
    public string RunId { get; }
    public string FileName { get; }
    public string ContentSha256Hex { get; }
    public string ContentDigest { get; }
    public long TotalBytes { get; }
    public bool HasScreenshot { get; }
}

public sealed class BundleOutboxRecord
{
    public BundleOutboxRecord(
        string bundleId,
        string runId,
        string fileName,
        string contentSha256Hex,
        string contentDigest,
        long totalBytes
    )
    {
        BundleId = bundleId;
        RunId = runId;
        FileName = fileName;
        ContentSha256Hex = contentSha256Hex;
        ContentDigest = contentDigest;
        TotalBytes = totalBytes;
    }

    public string BundleId { get; }
    public string RunId { get; }
    public string FileName { get; }
    public string ContentSha256Hex { get; }
    public string ContentDigest { get; }
    public long TotalBytes { get; }
}

public sealed class BundleOutboxStateRecord
{
    public BundleOutboxStateRecord(string bundleId, string runId, string fileName)
    {
        BundleId = bundleId;
        RunId = runId;
        FileName = fileName;
    }

    public string BundleId { get; }
    public string RunId { get; }
    public string FileName { get; }
}

public sealed class BundleUploadOutcomeRecord
{
    public BundleUploadOutcomeRecord(
        bool uploaded,
        string code,
        string? detail,
        string? requestId,
        string? outcome
    )
    {
        Uploaded = uploaded;
        Code = code;
        Detail = detail;
        RequestId = requestId;
        Outcome = outcome;
    }

    public bool Uploaded { get; }
    public string Code { get; }
    public string? Detail { get; }
    public string? RequestId { get; }
    public string? Outcome { get; }
}
