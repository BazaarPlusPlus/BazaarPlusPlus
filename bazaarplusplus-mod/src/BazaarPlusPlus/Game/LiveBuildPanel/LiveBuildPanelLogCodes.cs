#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal enum LiveBuildMountFailureReasonCode
{
    OverlayHostUnavailable,
}

internal enum LiveBuildRefreshResultCode
{
    Updated,
    NoChange,
}

internal enum LiveBuildRefreshFailureReasonCode
{
    RemoteEmptyResponse,
    RemoteInvalidResponse,
    RemoteRequestFailed,
    RefreshException,
}

internal enum LiveBuildCorpusSource
{
    Cache,
    Embedded,
    Remote,
    Unavailable,
}

internal enum LiveBuildCorpusReasonCode
{
    WarmupFailed,
    StaleCache,
    EmbeddedFallback,
    EmbeddedMissing,
    EmbeddedInvalid,
    CacheReadFailed,
    CacheInvalid,
    RefreshQueueFailed,
    RemoteRefreshFailed,
}

internal enum LiveBuildCorpusEndpoint
{
    TenWinBuilds,
}

internal enum LiveBuildCacheWriteReasonCode
{
    WriteFailed,
}

internal enum LiveBuildSnapshotReasonCode
{
    ReadException,
    InvalidPlacement,
}
