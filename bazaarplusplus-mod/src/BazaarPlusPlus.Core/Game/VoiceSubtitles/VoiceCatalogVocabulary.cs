#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

public enum VoiceCatalogSource
{
    None,
    Cache,
    Embedded,
    Remote,
}

public enum VoiceCatalogEndpoint
{
    VoiceCatalog,
}

public enum VoiceCatalogReasonCode
{
    CacheMissing,
    CacheStale,
    SourceRejected,
    NoUsableCatalog,
    WarmUpException,
    RefreshQueueFailed,
    RemoteFailed,
    EmptyResponse,
    WriteFailed,
}

public enum VoiceCatalogRowSkipReason
{
    MissingStem,
    DuplicateStem,
    EmptyText,
}
