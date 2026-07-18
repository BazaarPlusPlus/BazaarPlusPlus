#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

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

[BppLogEventSource]
internal static class VoiceObserverLogEvents
{
    internal static readonly BppLogFieldDefinition ObserverInstalledPlayerInstance = Public(
        0,
        "player_instance",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition ObserverInstalled = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.observer.installed",
        [ObserverInstalledPlayerInstance]
    );

    internal static readonly BppLogFieldDefinition AttemptStartedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition AttemptStartedOrigin = Public(
        1,
        "origin",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptStartedPlayerInstance = Public(
        2,
        "player_instance",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AttemptStartedSource = Public(
        3,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptStartedHook = Public(
        4,
        "hook",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptStartedEventReference = Untrusted(
        5,
        "event_ref"
    );
    internal static readonly BppLogFieldDefinition AttemptStartedEventPath = Untrusted(
        6,
        "event_path"
    );
    internal static readonly BppLogFieldDefinition AttemptStartedEventDurationMs = Public(
        7,
        "event_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition AttemptStarted = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.attempt.started",
        [
            AttemptStartedAttemptId,
            AttemptStartedOrigin,
            AttemptStartedPlayerInstance,
            AttemptStartedSource,
            AttemptStartedHook,
            AttemptStartedEventReference,
            AttemptStartedEventPath,
            AttemptStartedEventDurationMs,
        ]
    );

    internal static readonly BppLogFieldDefinition ObserverDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ObserverDegradedOrigin = Public(
        1,
        "origin",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ObserverDegradedHook = Public(
        2,
        "hook",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ObserverDegradedEventReference = Untrusted(
        3,
        "event_ref"
    );
    internal static readonly BppLogFieldDefinition ObserverDegradedCallbackEvent = Untrusted(
        4,
        "callback_event"
    );
    internal static readonly BppLogEventDefinition ObserverDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.observer.degraded",
        [
            ObserverDegradedReasonCode,
            ObserverDegradedOrigin,
            ObserverDegradedHook,
            ObserverDegradedEventReference,
            ObserverDegradedCallbackEvent,
        ],
        new BppLogStormPolicy([ObserverDegradedReasonCode, ObserverDegradedHook])
    );

    internal static readonly BppLogFieldDefinition AttemptClearedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition AttemptClearedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptClearedAgeMs = Public(
        2,
        "age_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition AttemptCleared = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.attempt.cleared",
        [AttemptClearedAttemptId, AttemptClearedReasonCode, AttemptClearedAgeMs]
    );

    internal static readonly BppLogFieldDefinition SoundObservedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition SoundObservedPlayerInstance = Public(
        1,
        "player_instance",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SoundObservedContextPlayerInstance = Public(
        2,
        "context_player_instance",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SoundObservedSource = Public(
        3,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SoundObservedHook = Public(
        4,
        "hook",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SoundObservedSoundName = Untrusted(
        5,
        "sound_name"
    );
    internal static readonly BppLogFieldDefinition SoundObservedSoundDurationMs = Public(
        6,
        "sound_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SoundObservedEventPath = Untrusted(
        7,
        "event_path"
    );
    internal static readonly BppLogEventDefinition SoundObserved = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.sound.observed",
        [
            SoundObservedAttemptId,
            SoundObservedPlayerInstance,
            SoundObservedContextPlayerInstance,
            SoundObservedSource,
            SoundObservedHook,
            SoundObservedSoundName,
            SoundObservedSoundDurationMs,
            SoundObservedEventPath,
        ]
    );

    internal static readonly BppLogFieldDefinition LookupSkippedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition LookupSkippedOrigin = Public(
        1,
        "origin",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LookupSkippedStrategy = Public(
        2,
        "strategy",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LookupSkippedHook = Public(
        3,
        "hook",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LookupSkippedSoundName = Untrusted(
        4,
        "sound_name"
    );
    internal static readonly BppLogFieldDefinition LookupSkippedReasonCode = Public(
        5,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition LookupSkipped = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.lookup.skipped",
        [
            LookupSkippedAttemptId,
            LookupSkippedOrigin,
            LookupSkippedStrategy,
            LookupSkippedHook,
            LookupSkippedSoundName,
            LookupSkippedReasonCode,
        ]
    );

    internal static readonly BppLogFieldDefinition LookupResolvedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition LookupResolvedOrigin = Public(
        1,
        "origin",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LookupResolvedStrategy = Public(
        2,
        "strategy",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LookupResolvedCatalog = Public(
        3,
        "catalog",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition LookupResolvedMatchedToken = Untrusted(
        4,
        "matched_token"
    );
    internal static readonly BppLogFieldDefinition LookupResolvedCandidateCount = Public(
        5,
        "candidate_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LookupResolvedStem = Public(
        6,
        "stem",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LookupResolvedSoundDurationMs = Public(
        7,
        "sound_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LookupResolvedEventDurationMs = Public(
        8,
        "event_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LookupResolvedLineDurationMs = Public(
        9,
        "line_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LookupResolvedDisplayDurationMs = Public(
        10,
        "display_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition LookupResolvedEnglishText = Untrusted(
        11,
        "english_text"
    );
    internal static readonly BppLogFieldDefinition LookupResolvedChineseText = Untrusted(
        12,
        "chinese_text"
    );
    internal static readonly BppLogEventDefinition LookupResolved = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.lookup.resolved",
        [
            LookupResolvedAttemptId,
            LookupResolvedOrigin,
            LookupResolvedStrategy,
            LookupResolvedCatalog,
            LookupResolvedMatchedToken,
            LookupResolvedCandidateCount,
            LookupResolvedStem,
            LookupResolvedSoundDurationMs,
            LookupResolvedEventDurationMs,
            LookupResolvedLineDurationMs,
            LookupResolvedDisplayDurationMs,
            LookupResolvedEnglishText,
            LookupResolvedChineseText,
        ]
    );

    internal static readonly BppLogFieldDefinition AttemptStoppedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition AttemptStoppedPlayerInstance = Public(
        1,
        "player_instance",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AttemptStoppedAgeMs = Public(
        2,
        "age_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition AttemptStopped = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.attempt.stopped",
        [AttemptStoppedAttemptId, AttemptStoppedPlayerInstance, AttemptStoppedAgeMs]
    );

    internal static readonly BppLogFieldDefinition CallbackObservedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition CallbackObservedOrigin = Public(
        1,
        "origin",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CallbackObservedAgeMs = Public(
        2,
        "age_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CallbackObservedSource = Public(
        3,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CallbackObservedHook = Public(
        4,
        "hook",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CallbackObservedContextMatchesCallback = Public(
        5,
        "context_matches_callback",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CallbackObservedCallbackEvent = Untrusted(
        6,
        "callback_event"
    );
    internal static readonly BppLogFieldDefinition CallbackObservedContextEventReference =
        Untrusted(7, "context_event_ref");
    internal static readonly BppLogFieldDefinition CallbackObservedContextEventPath = Untrusted(
        8,
        "context_event_path"
    );
    internal static readonly BppLogEventDefinition CallbackObserved = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.callback.observed",
        [
            CallbackObservedAttemptId,
            CallbackObservedOrigin,
            CallbackObservedAgeMs,
            CallbackObservedSource,
            CallbackObservedHook,
            CallbackObservedContextMatchesCallback,
            CallbackObservedCallbackEvent,
            CallbackObservedContextEventReference,
            CallbackObservedContextEventPath,
        ]
    );

    internal static readonly BppLogFieldDefinition LookupFailedAttemptId = Public(
        0,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition LookupFailedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition LookupFailed = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.lookup.failed",
        [LookupFailedAttemptId, LookupFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition GateDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition GateDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.gate.degraded",
        [GateDegradedReasonCode],
        new BppLogStormPolicy([GateDegradedReasonCode])
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);

    private static BppLogFieldDefinition Untrusted(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
