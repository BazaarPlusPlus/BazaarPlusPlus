#nullable enable

namespace BazaarPlusPlus.Game.EventPreview;

public enum EventPreviewPlanSource
{
    Unknown,
    Cache,
    Rebuild,
}

public enum EventPreviewPlanReasonCode
{
    None,
    SourceInfoUnavailable,
    LoadException,
    PartialCoverage,
    CacheWriteException,
}
