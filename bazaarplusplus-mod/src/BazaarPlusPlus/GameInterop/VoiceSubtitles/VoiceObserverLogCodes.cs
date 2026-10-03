#nullable enable

namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal enum VoiceObserverLogOrigin
{
    Unknown,
    PlayVo,
    PlayTutorialVo,
}

internal enum VoiceObserverLogSource
{
    Unknown,
    Hero,
    Merchant,
}

internal enum VoiceObserverLogReasonCode
{
    HookInspectionFailed,
    EventMetadataUnavailable,
    AttemptContextUnavailable,
    PlayVoCompleted,
    PlayTutorialVoCompleted,
    SoundNameUnavailable,
    SoundDurationUnavailable,
    NoMatch,
    LookupCallbackFailed,
    EnabledCheckFailed,
}
