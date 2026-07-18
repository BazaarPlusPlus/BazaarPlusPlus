#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum HistoryPanelCancellationDisposition
{
    AbandonStaleRequest,
    FailCurrentRequest,
}

internal static class HistoryPanelCancellationRouter
{
    internal static HistoryPanelCancellationDisposition Resolve(bool isCurrentSession) =>
        isCurrentSession
            ? HistoryPanelCancellationDisposition.FailCurrentRequest
            : HistoryPanelCancellationDisposition.AbandonStaleRequest;
}

internal enum HistoryPanelMountDependency
{
    CombatReplayRuntime,
    OverlayPanelHost,
    OnlineClient,
}

internal enum HistoryPanelMountReasonCode
{
    DependencyUnavailable,
}

internal enum HistoryPanelDataset
{
    RecentRuns,
    GhostBattles,
    SelectedRunBattles,
}

internal enum HistoryPanelReplayReasonCode
{
    Completed,
    RecordingAvailable,
    RecordingUnavailable,
    RuntimeUnavailable,
    ReplayUnavailable,
    ReplayRejected,
    ReplayDirectoryUnavailable,
    GhostDownloadUnavailable,
    GhostDownloadLinkFailed,
    GhostDownloadFailed,
    GhostArtifactInvalid,
    GhostBattleMismatch,
    GhostManifestUnavailable,
    ReplayPayloadMissing,
    ReplayPayloadInvalid,
    ReplayPayloadUnreadable,
    UnexpectedException,
    Canceled,
}

internal enum HistoryPanelRunDeleteReasonCode
{
    Completed,
    PrimaryDeleteFailed,
    ReplayPayloadCleanupFailed,
}

internal enum HistoryPanelServerHealthReasonCode
{
    Completed,
    HttpFailure,
    HealthStatusNotOk,
    ServerTimeInvalid,
    TransportFailure,
    UnexpectedException,
    Canceled,
}

internal enum HistoryPanelGhostSyncReasonCode
{
    Completed,
    SyncUnavailable,
    IdentityUnavailable,
    QueryFailed,
    RepositoryFailed,
    UnexpectedException,
    Canceled,
}

internal enum HistoryPanelGhostIdentityReasonCode
{
    ClientCacheReadFailed,
}

internal enum HistoryPanelPreviewReasonCode
{
    SocketEffectLookupFailed,
    StaticDataAccessFailed,
    StaticDataUnavailable,
}

internal enum HistoryPanelPreviewPayloadReasonCode
{
    PayloadInvalid,
    PayloadUnreadable,
}

internal enum HistoryPanelRowReasonCode
{
    SnapshotDeserializeFailed,
}

internal enum HistoryPanelOpenReasonCode
{
    InstanceUnavailable,
    OverlayHandleUnavailable,
    UnknownPanel,
    CombatActive,
    RequestException,
}

internal readonly record struct HistoryPanelReplayPreflightResult(
    string RequestId,
    string BattleId,
    bool RecordVideo,
    bool CanRecord,
    HistoryPanelReplayReasonCode ReasonCode
);

internal readonly record struct HistoryPanelReplayAcceptedResult(
    string RequestId,
    string BattleId,
    bool RecordVideo
);

internal readonly record struct HistoryPanelReplayFailedResult(
    string RequestId,
    string BattleId,
    bool RecordVideo,
    HistoryPanelReplayReasonCode ReasonCode,
    Exception? Exception
);

internal sealed class HistoryPanelReplayLogOperation
{
    private readonly string _requestId;
    private readonly string _battleId;
    private readonly bool _recordVideo;
    private int _preflightRecorded;
    private int _terminal;

    internal HistoryPanelReplayLogOperation(string requestId, string battleId, bool recordVideo)
    {
        _requestId = requestId;
        _battleId = battleId;
        _recordVideo = recordVideo;
    }

    internal bool TryRecordPreflight(
        bool canRecord,
        HistoryPanelReplayReasonCode reasonCode,
        out HistoryPanelReplayPreflightResult result
    )
    {
        if (Interlocked.CompareExchange(ref _preflightRecorded, 1, 0) != 0)
        {
            result = default;
            return false;
        }

        result = new(_requestId, _battleId, _recordVideo, canRecord, reasonCode);
        return true;
    }

    internal bool TryAccept(out HistoryPanelReplayAcceptedResult result)
    {
        if (!TryComplete())
        {
            result = default;
            return false;
        }

        result = new(_requestId, _battleId, _recordVideo);
        return true;
    }

    internal bool TryFail(
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception,
        out HistoryPanelReplayFailedResult result
    )
    {
        if (!TryComplete())
        {
            result = default;
            return false;
        }

        result = new(_requestId, _battleId, _recordVideo, reasonCode, exception);
        return true;
    }

    internal void Abandon() => TryComplete();

    private bool TryComplete() => Interlocked.CompareExchange(ref _terminal, 1, 0) == 0;
}

internal enum HistoryPanelRunDeleteTerminalStatus
{
    Failed,
    Degraded,
    Succeeded,
}

internal readonly record struct HistoryPanelRunDeleteTerminalResult(
    HistoryPanelRunDeleteTerminalStatus Status,
    string RequestId,
    string RunId,
    int BattleCount,
    int CleanupFailedCount,
    HistoryPanelRunDeleteReasonCode ReasonCode,
    Exception? Exception
);

internal sealed class HistoryPanelRunDeleteLogOperation
{
    private readonly string _requestId;
    private readonly string _runId;
    private int _terminal;

    internal HistoryPanelRunDeleteLogOperation(string requestId, string runId)
    {
        _requestId = requestId;
        _runId = runId;
    }

    internal bool TryComplete(
        HistoryPanelRunDeleteTerminalStatus status,
        int battleCount,
        int cleanupFailedCount,
        HistoryPanelRunDeleteReasonCode reasonCode,
        Exception? exception,
        out HistoryPanelRunDeleteTerminalResult result
    )
    {
        if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
        {
            result = default;
            return false;
        }

        result = new(
            status,
            _requestId,
            _runId,
            Math.Max(0, battleCount),
            Math.Max(0, cleanupFailedCount),
            reasonCode,
            exception
        );
        return true;
    }
}

internal enum HistoryPanelServerHealthTerminalStatus
{
    Failed,
    Succeeded,
}

internal readonly record struct HistoryPanelServerHealthTerminalResult(
    HistoryPanelServerHealthTerminalStatus Status,
    string RequestId,
    long DurationMilliseconds,
    HistoryPanelServerHealthReasonCode ReasonCode,
    Exception? Exception
);

internal sealed class HistoryPanelServerHealthLogOperation
{
    private readonly string _requestId;
    private readonly Func<long> _clock;
    private readonly long _startedAt;
    private int _terminal;

    internal HistoryPanelServerHealthLogOperation(string requestId, Func<long>? clock = null)
    {
        _requestId = requestId;
        _clock = clock ?? MonotonicMilliseconds;
        _startedAt = _clock();
    }

    internal bool TryComplete(
        HistoryPanelServerHealthTerminalStatus status,
        HistoryPanelServerHealthReasonCode reasonCode,
        Exception? exception,
        out HistoryPanelServerHealthTerminalResult result
    )
    {
        if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
        {
            result = default;
            return false;
        }

        result = new(status, _requestId, Math.Max(0, _clock() - _startedAt), reasonCode, exception);
        return true;
    }

    internal void Abandon() => Interlocked.CompareExchange(ref _terminal, 1, 0);

    private static long MonotonicMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);
}

internal enum HistoryPanelGhostSyncTerminalStatus
{
    Failed,
    Succeeded,
}

internal readonly record struct HistoryPanelGhostSyncTerminalResult(
    HistoryPanelGhostSyncTerminalStatus Status,
    string RequestId,
    int ImportedCount,
    HistoryPanelGhostSyncReasonCode ReasonCode,
    Exception? Exception
);

internal sealed class HistoryPanelGhostSyncLogOperation
{
    private readonly string _requestId;
    private int _terminal;

    internal HistoryPanelGhostSyncLogOperation(string requestId)
    {
        _requestId = requestId;
    }

    internal bool TrySucceed(int importedCount, out HistoryPanelGhostSyncTerminalResult result) =>
        TryComplete(
            HistoryPanelGhostSyncTerminalStatus.Succeeded,
            importedCount,
            HistoryPanelGhostSyncReasonCode.Completed,
            exception: null,
            out result
        );

    internal bool TryFail(
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception,
        out HistoryPanelGhostSyncTerminalResult result
    ) =>
        TryComplete(
            HistoryPanelGhostSyncTerminalStatus.Failed,
            0,
            reasonCode,
            exception,
            out result
        );

    internal void Abandon() => Interlocked.CompareExchange(ref _terminal, 1, 0);

    private bool TryComplete(
        HistoryPanelGhostSyncTerminalStatus status,
        int importedCount,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception,
        out HistoryPanelGhostSyncTerminalResult result
    )
    {
        if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
        {
            result = default;
            return false;
        }

        result = new(status, _requestId, Math.Max(0, importedCount), reasonCode, exception);
        return true;
    }
}

internal static class HistoryPanelServerHealthReasonClassifier
{
    internal static HistoryPanelServerHealthReasonCode Classify(string? error)
    {
        if (error?.StartsWith("http_", StringComparison.OrdinalIgnoreCase) == true)
            return HistoryPanelServerHealthReasonCode.HttpFailure;
        if (string.Equals(error, "health_status_not_ok", StringComparison.OrdinalIgnoreCase))
            return HistoryPanelServerHealthReasonCode.HealthStatusNotOk;
        if (string.Equals(error, "server_time_invalid", StringComparison.OrdinalIgnoreCase))
            return HistoryPanelServerHealthReasonCode.ServerTimeInvalid;
        return HistoryPanelServerHealthReasonCode.TransportFailure;
    }
}
