#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Patches.VoiceSubtitles;

internal enum VoicePatchLogReasonCode
{
    ObserverInstallFailed,
    CallbackApiUnavailable,
    PatchCountMismatch,
}

[BppLogEventSource]
internal static class VoicePatchLogEvents
{
    internal static readonly BppLogFieldDefinition ObserverFailedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ObserverFailed = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.observer.failed",
        [ObserverFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition CallbackPatchDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CallbackPatchDegradedActualCount = Public(
        1,
        "actual_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CallbackPatchDegradedExpectedCount = Public(
        2,
        "expected_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CallbackPatchDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.callback_patch.degraded",
        [
            CallbackPatchDegradedReasonCode,
            CallbackPatchDegradedActualCount,
            CallbackPatchDegradedExpectedCount,
        ]
    );

    internal static readonly BppLogFieldDefinition CallbackPatchReadyActualCount = Public(
        0,
        "actual_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CallbackPatchReadyExpectedCount = Public(
        1,
        "expected_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CallbackPatchReady = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.callback_patch.ready",
        [CallbackPatchReadyActualCount, CallbackPatchReadyExpectedCount]
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);
}
