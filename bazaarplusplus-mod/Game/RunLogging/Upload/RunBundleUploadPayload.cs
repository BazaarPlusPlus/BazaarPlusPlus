#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.Online.Models;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadSnapshot
{
    public RunBundleUploadRequestV3 Payload { get; set; } = new();

    public string RunId { get; set; } = string.Empty;

    public long LastSeq { get; set; }

    public string? UploadedStatus { get; set; }

    public IReadOnlyList<string> BattleIds { get; set; } = new List<string>();
}

internal readonly struct RunBundleUploadCycleResult
{
    public RunBundleUploadCycleResult(int uploadedCount, bool hasMorePending)
    {
        UploadedCount = uploadedCount;
        HasMorePending = hasMorePending;
    }

    public int UploadedCount { get; }

    public bool HasMorePending { get; }
}
