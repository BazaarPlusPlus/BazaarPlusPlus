#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal enum VoiceCatalogSource
{
    None,
    Cache,
    Embedded,
    Remote,
}

internal enum VoiceCatalogEndpoint
{
    VoiceCatalog,
}

internal enum VoiceCatalogReasonCode
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

internal enum VoiceCatalogRowSkipReason
{
    MissingStem,
    DuplicateStem,
    EmptyText,
}
