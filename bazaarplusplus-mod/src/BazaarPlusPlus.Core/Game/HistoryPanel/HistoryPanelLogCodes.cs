#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

public enum HistoryPanelMountDependency
{
    CombatReplayRuntime,
    OverlayPanelHost,
}

public enum HistoryPanelMountReasonCode
{
    DependencyUnavailable,
}

public enum HistoryPanelReplayReasonCode
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

public enum HistoryPanelRunDeleteReasonCode
{
    Completed,
    PrimaryDeleteFailed,
    ReplayPayloadCleanupFailed,
}

public enum HistoryPanelServerHealthReasonCode
{
    Completed,
    HttpFailure,
    HealthStatusNotOk,
    ServerTimeInvalid,
    TransportFailure,
    UnexpectedException,
    Canceled,
}

public enum HistoryPanelGhostSyncReasonCode
{
    Completed,
    SyncUnavailable,
    IdentityUnavailable,
    QueryFailed,
    RepositoryFailed,
    UnexpectedException,
    Canceled,
}

public enum HistoryPanelGhostIdentityReasonCode
{
    ClientCacheReadFailed,
}

public enum HistoryPanelPreviewReasonCode
{
    SocketEffectLookupFailed,
    StaticDataAccessFailed,
    StaticDataUnavailable,
}

public enum HistoryPanelPreviewPayloadReasonCode
{
    PayloadInvalid,
    PayloadUnreadable,
}

public enum HistoryPanelOpenReasonCode
{
    InstanceUnavailable,
    OverlayHandleUnavailable,
    UnknownPanel,
    CombatActive,
    RequestException,
}

public static class HistoryPanelServerHealthReasonClassifier
{
    public static HistoryPanelServerHealthReasonCode Classify(string? error)
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
