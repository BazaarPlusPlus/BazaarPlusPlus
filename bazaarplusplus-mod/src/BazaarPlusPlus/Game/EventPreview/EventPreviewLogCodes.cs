#nullable enable

namespace BazaarPlusPlus.Game.EventPreview;

internal enum EventPreviewPlanSource
{
    Unknown,
    Cache,
    Rebuild,
}

internal enum EventPreviewPlanReasonCode
{
    None,
    SourceInfoUnavailable,
    LoadException,
    PartialCoverage,
    CacheWriteException,
}
