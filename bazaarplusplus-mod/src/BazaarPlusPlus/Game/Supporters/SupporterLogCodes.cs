#nullable enable

namespace BazaarPlusPlus.Game.Supporters;

internal enum SupporterCatalogSource
{
    Remote,
    DiskCache,
    BundledFallback,
}

internal enum SupporterLogReasonCode
{
    EmptyPayload,
    ReadException,
    RefreshException,
    WriteException,
}

internal enum SupporterCacheOperation
{
    Write,
}

internal enum SupporterCatalogOperation
{
    Load,
}
