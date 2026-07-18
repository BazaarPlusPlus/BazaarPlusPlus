#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal enum VoiceSubtitlesLogReasonCode
{
    Mount,
    MountException,
    EmptyText,
    SettingsApplyException,
    PlaybackStopped,
    FallbackTimeout,
    PlaybackQueryException,
}

internal enum VoiceSubtitlesSettingsPhase
{
    Mount,
    Show,
}

[BppLogEventSource]
internal static class VoiceSubtitlesDisplayLogEvents
{
    internal static readonly BppLogFieldDefinition FontEnvironmentReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FontEnvironmentAnchorPath = UntrustedField(
        1,
        "anchor_path",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition FontEnvironmentSourceFont = UntrustedField(
        2,
        "source_font",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition FontEnvironmentSourceCoverage = PublicField(
        3,
        "source_coverage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FontEnvironmentDefaultFont = UntrustedField(
        4,
        "default_font",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition FontEnvironmentFallbackFonts = UntrustedField(
        5,
        "fallback_fonts",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition FontEnvironmentObserved = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.font_environment.observed",
        [
            FontEnvironmentReasonCode,
            FontEnvironmentAnchorPath,
            FontEnvironmentSourceFont,
            FontEnvironmentSourceCoverage,
            FontEnvironmentDefaultFont,
            FontEnvironmentFallbackFonts,
        ]
    );

    internal static readonly BppLogFieldDefinition FontInventoryCount = PublicField(
        0,
        "font_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition FontInventoryFonts = UntrustedField(
        1,
        "fonts",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition FontInventoryObserved = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.font_inventory.observed",
        [FontInventoryCount, FontInventoryFonts]
    );

    internal static readonly BppLogFieldDefinition MountAnchorPath = UntrustedField(
        0,
        "anchor_path",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition MountAnchorLabelText = UntrustedField(
        1,
        "label_text",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition MountAnchorSelected = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.mount_anchor.selected",
        [MountAnchorPath, MountAnchorLabelText]
    );

    internal static readonly BppLogFieldDefinition OverlayMountedRenderer = UntrustedField(
        0,
        "renderer",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition OverlayMountedAnchorPath = UntrustedField(
        1,
        "anchor_path",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition OverlayMounted = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.overlay.mounted",
        [OverlayMountedRenderer, OverlayMountedAnchorPath]
    );

    internal static readonly BppLogFieldDefinition OverlayFailedStage = PublicField(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition OverlayFailedAnchorPath = UntrustedField(
        1,
        "anchor_path",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Hash
    );
    internal static readonly BppLogFieldDefinition OverlayFailedAnchorText = UntrustedField(
        2,
        "anchor_text",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition OverlayFailedReasonCode = PublicField(
        3,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition OverlayFailed = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.overlay.failed",
        [
            OverlayFailedStage,
            OverlayFailedAnchorPath,
            OverlayFailedAnchorText,
            OverlayFailedReasonCode,
        ],
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogFieldDefinition DisplaySkippedDisplayId = PublicField(
        0,
        "display_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition DisplaySkippedAttemptId = PublicField(
        1,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition DisplaySkippedStem = PublicField(
        2,
        "stem",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplaySkippedReasonCode = PublicField(
        3,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition DisplaySkipped = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.display.skipped",
        [
            DisplaySkippedDisplayId,
            DisplaySkippedAttemptId,
            DisplaySkippedStem,
            DisplaySkippedReasonCode,
        ]
    );

    internal static readonly BppLogFieldDefinition DisplayRenderedDisplayId = PublicField(
        0,
        "display_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedAttemptId = PublicField(
        1,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedStem = PublicField(
        2,
        "stem",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedEventDurationMs = PublicField(
        3,
        "event_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedLineDurationMs = PublicField(
        4,
        "line_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedDisplayDurationMs = PublicField(
        5,
        "display_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedRenderer = UntrustedField(
        6,
        "renderer",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedActiveBefore = PublicField(
        7,
        "active_before",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedPlaybackState = UntrustedField(
        8,
        "playback_state",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedEnglishText = UntrustedField(
        9,
        "english_text",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayRenderedChineseText = UntrustedField(
        10,
        "chinese_text",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition DisplayRendered = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.display.rendered",
        [
            DisplayRenderedDisplayId,
            DisplayRenderedAttemptId,
            DisplayRenderedStem,
            DisplayRenderedEventDurationMs,
            DisplayRenderedLineDurationMs,
            DisplayRenderedDisplayDurationMs,
            DisplayRenderedRenderer,
            DisplayRenderedActiveBefore,
            DisplayRenderedPlaybackState,
            DisplayRenderedEnglishText,
            DisplayRenderedChineseText,
        ]
    );

    internal static readonly BppLogFieldDefinition SettingsDegradedPhase = PublicField(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SettingsDegradedReasonCode = PublicField(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition SettingsDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.settings.degraded",
        [SettingsDegradedPhase, SettingsDegradedReasonCode],
        new BppLogStormPolicy([SettingsDegradedPhase, SettingsDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition SettingsRecoveredPhase = PublicField(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition SettingsRecovered = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.settings.recovered",
        [SettingsRecoveredPhase]
    );

    internal static readonly BppLogFieldDefinition DisplayHiddenDisplayId = PublicField(
        0,
        "display_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition DisplayHiddenAttemptId = PublicField(
        1,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition DisplayHiddenStem = PublicField(
        2,
        "stem",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayHiddenReasonCode = PublicField(
        3,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DisplayHiddenElapsedMs = PublicField(
        4,
        "elapsed_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DisplayHiddenPlaybackState = UntrustedField(
        5,
        "playback_state",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition DisplayHidden = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.display.hidden",
        [
            DisplayHiddenDisplayId,
            DisplayHiddenAttemptId,
            DisplayHiddenStem,
            DisplayHiddenReasonCode,
            DisplayHiddenElapsedMs,
            DisplayHiddenPlaybackState,
        ]
    );

    internal static readonly BppLogFieldDefinition PlaybackTrackingDegradedDisplayId = PublicField(
        0,
        "display_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition PlaybackTrackingDegradedAttemptId = PublicField(
        1,
        "attempt_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Full
    );
    internal static readonly BppLogFieldDefinition PlaybackTrackingDegradedReasonCode = PublicField(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PlaybackTrackingDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.playback_tracking.degraded",
        [
            PlaybackTrackingDegradedDisplayId,
            PlaybackTrackingDegradedAttemptId,
            PlaybackTrackingDegradedReasonCode,
        ],
        new BppLogStormPolicy([PlaybackTrackingDegradedReasonCode])
    );

    private static BppLogFieldDefinition PublicField(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);

    private static BppLogFieldDefinition UntrustedField(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.UntrustedText, correlation, cardinality);
}

internal sealed class VoiceSubtitlesSettingsLogState
{
    private readonly bool[] _degradedByPhase = new bool[2];

    internal void ReportDegraded(VoiceSubtitlesSettingsPhase phase, Exception exception)
    {
        var phaseIndex = (int)phase;
        if (_degradedByPhase[phaseIndex])
            return;

        _degradedByPhase[phaseIndex] = true;
        BppLog.WarnEvent(
            VoiceSubtitlesDisplayLogEvents.SettingsDegraded,
            exception,
            VoiceSubtitlesDisplayLogEvents.SettingsDegradedPhase.Bind(phase),
            VoiceSubtitlesDisplayLogEvents.SettingsDegradedReasonCode.Bind(
                VoiceSubtitlesLogReasonCode.SettingsApplyException
            )
        );
    }

    internal void ReportSucceeded(VoiceSubtitlesSettingsPhase phase)
    {
        var phaseIndex = (int)phase;
        if (!_degradedByPhase[phaseIndex])
            return;

        _degradedByPhase[phaseIndex] = false;
        BppLog.RecoverStorm(
            VoiceSubtitlesDisplayLogEvents.SettingsDegraded,
            VoiceSubtitlesDisplayLogEvents.SettingsDegradedPhase.Bind(phase),
            VoiceSubtitlesDisplayLogEvents.SettingsDegradedReasonCode.Bind(
                VoiceSubtitlesLogReasonCode.SettingsApplyException
            )
        );
        BppLog.InfoEvent(
            VoiceSubtitlesDisplayLogEvents.SettingsRecovered,
            VoiceSubtitlesDisplayLogEvents.SettingsRecoveredPhase.Bind(phase)
        );
    }
}
