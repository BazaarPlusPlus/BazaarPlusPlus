#nullable enable
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadSnapshot
{
    public RunBundleUploadRequest Metadata { get; set; } = new();

    public byte[] ArtifactBytes { get; set; } = [];

    public string RunId { get; set; } = string.Empty;

    public long LastSeq { get; set; }

    public string? UploadedStatus { get; set; }

    public IReadOnlyList<string> BattleIds { get; set; } = new List<string>();
}

internal enum RunBundleBuildStatus
{
    Ready,
    NotReady,
    IntegrityFailed,
}

internal sealed class RunBundleBuildResult
{
    private RunBundleBuildResult(
        RunBundleBuildStatus status,
        RunBundleUploadSnapshot? snapshot,
        UploadLogReasonCode? reasonCode
    )
    {
        Status = status;
        Snapshot = snapshot;
        ReasonCode = reasonCode;
    }

    internal RunBundleBuildStatus Status { get; }
    internal RunBundleUploadSnapshot? Snapshot { get; }
    internal UploadLogReasonCode? ReasonCode { get; }

    internal static RunBundleBuildResult Ready(RunBundleUploadSnapshot snapshot) =>
        new(RunBundleBuildStatus.Ready, snapshot, null);

    internal static RunBundleBuildResult NotReady() =>
        new(RunBundleBuildStatus.NotReady, null, null);

    internal static RunBundleBuildResult IntegrityFailed(UploadLogReasonCode reasonCode) =>
        new(RunBundleBuildStatus.IntegrityFailed, null, reasonCode);
}
