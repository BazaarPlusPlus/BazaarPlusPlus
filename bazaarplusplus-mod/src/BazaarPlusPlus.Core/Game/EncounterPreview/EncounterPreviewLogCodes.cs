#nullable enable

namespace BazaarPlusPlus.Game.EncounterPreview;

public enum EncounterPreviewPlanSource
{
    Unknown,
    Cache,
    Rebuild,
}

public enum EncounterPreviewPlanReasonCode
{
    None,
    SourceInfoUnavailable,
    LoadException,
    PartialCoverage,
    CacheWriteException,
}
