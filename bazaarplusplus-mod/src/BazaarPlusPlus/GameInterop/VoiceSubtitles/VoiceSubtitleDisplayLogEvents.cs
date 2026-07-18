#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal enum VoiceSubtitleDisplayLogReasonCode
{
    LabelUnavailable,
    QueueFailed,
}

[BppLogEventSource]
internal static class VoiceSubtitleDisplayLogEvents
{
    internal static readonly BppLogFieldDefinition DisplayId = new(
        0,
        "display_id",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.Full,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition AttemptId = new(
        1,
        "attempt_id",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.Full,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition Stem = new(
        2,
        "stem",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition ReasonCode = new(
        3,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition DisplayFailed = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.display.failed",
        [DisplayId, AttemptId, Stem, ReasonCode]
    );
}
