#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

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
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.settings.degraded",
                storm: ["phase", "reason_code"]
            ),
            exception,
            ("phase", phase),
            ("reason_code", VoiceSubtitlesLogReasonCode.SettingsApplyException)
        );
    }

    internal void ReportSucceeded(VoiceSubtitlesSettingsPhase phase)
    {
        var phaseIndex = (int)phase;
        if (!_degradedByPhase[phaseIndex])
            return;

        _degradedByPhase[phaseIndex] = false;
        BppLog.RecoverStorm(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.settings.degraded",
                storm: ["phase", "reason_code"]
            ),
            ("phase", phase),
            ("reason_code", VoiceSubtitlesLogReasonCode.SettingsApplyException)
        );
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.settings.recovered"
            ),
            ("phase", phase)
        );
    }
}
