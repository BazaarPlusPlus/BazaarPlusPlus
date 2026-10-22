#nullable enable

namespace BazaarPlusPlus.Game.Supporters;

public enum SupporterCatalogSource
{
    Remote,
    DiskCache,
    BundledFallback,
}

public enum SupporterLogReasonCode
{
    EmptyPayload,
    ReadException,
    RefreshException,
    WriteException,
}

public enum SupporterCacheOperation
{
    Write,
}

public enum SupporterCatalogOperation
{
    Load,
}
