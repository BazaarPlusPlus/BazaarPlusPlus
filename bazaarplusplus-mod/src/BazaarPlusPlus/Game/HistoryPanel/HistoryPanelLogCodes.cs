#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum HistoryPanelMountDependency
{
    CombatReplayRuntime,
    OverlayPanelHost,
}

internal enum HistoryPanelMountReasonCode
{
    DependencyUnavailable,
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

internal enum HistoryPanelOpenReasonCode
{
    InstanceUnavailable,
    OverlayHandleUnavailable,
    UnknownPanel,
    CombatActive,
    RequestException,
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
