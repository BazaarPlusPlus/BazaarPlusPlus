#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel;

public enum LiveBuildMountFailureReasonCode
{
    OverlayHostUnavailable,
}

public enum LiveBuildRefreshResultCode
{
    Updated,
    NoChange,
}

public enum LiveBuildRefreshFailureReasonCode
{
    RemoteEmptyResponse,
    RemoteInvalidResponse,
    RemoteRequestFailed,
    RefreshException,
}

public enum LiveBuildCorpusSource
{
    Cache,
    Embedded,
    Remote,
    Unavailable,
}

public enum LiveBuildCorpusReasonCode
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

public enum LiveBuildCorpusEndpoint
{
    TenWinBuilds,
}

public enum LiveBuildCacheWriteReasonCode
{
    WriteFailed,
}
