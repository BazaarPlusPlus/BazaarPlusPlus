using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesLoggingTests
{
    [Fact]
    public void Display_event_catalog_matches_the_locked_manifest()
    {
        var actual = typeof(VoiceSubtitlesDisplayLogEvents)
            .GetFields(
                System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly
            )
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!)
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voice_subtitles.display.hidden"] =
                "display_id:High:Full|attempt_id:High:Full|stem:High:None|reason_code:Low:None|elapsed_ms:High:None|playback_state:Low:None",
            ["voice_subtitles.display.rendered"] =
                "display_id:High:Full|attempt_id:High:Full|stem:High:None|event_duration_ms:High:None|line_duration_ms:High:None|display_duration_ms:High:None|renderer:High:None|active_before:Low:None|playback_state:Low:None|english_text:High:None|chinese_text:High:None",
            ["voice_subtitles.display.skipped"] =
                "display_id:High:Full|attempt_id:High:Full|stem:High:None|reason_code:Low:None",
            ["voice_subtitles.font_environment.observed"] =
                "reason_code:Low:None|anchor_path:High:None|source_font:High:None|source_coverage:Low:None|default_font:High:None|fallback_fonts:High:None",
            ["voice_subtitles.font_inventory.observed"] = "font_count:High:None|fonts:High:None",
            ["voice_subtitles.mount_anchor.selected"] =
                "anchor_path:High:None|label_text:High:None",
            ["voice_subtitles.overlay.failed"] =
                "stage:Low:None|anchor_path:High:Hash|anchor_text:High:None|reason_code:Low:None",
            ["voice_subtitles.overlay.mounted"] = "renderer:High:None|anchor_path:High:None",
            ["voice_subtitles.playback_tracking.degraded"] =
                "display_id:High:Full|attempt_id:High:Full|reason_code:Low:None",
            ["voice_subtitles.settings.degraded"] = "phase:Low:None|reason_code:Low:None",
            ["voice_subtitles.settings.recovered"] = "phase:Low:None",
        };

        Assert.Equal(expected, actual);
        Assert.Empty(VoiceSubtitlesDisplayLogEvents.OverlayFailed.StormPolicy!.KeyFields);
        Assert.Equal(
            ["phase", "reason_code"],
            VoiceSubtitlesDisplayLogEvents.SettingsDegraded.StormPolicy!.KeyFields.Select(field =>
                field.Name
            )
        );
        Assert.Equal(
            ["reason_code"],
            VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegraded.StormPolicy!.KeyFields.Select(
                field => field.Name
            )
        );
    }

    [Fact]
    public void Settings_degradation_is_one_warning_episode_with_one_later_recovery()
    {
        using var capture = new LogCapture();
        var state = new VoiceSubtitlesSettingsLogState();

        state.ReportDegraded(
            VoiceSubtitlesSettingsPhase.Mount,
            new InvalidOperationException("first")
        );
        state.ReportDegraded(
            VoiceSubtitlesSettingsPhase.Mount,
            new InvalidOperationException("repeat")
        );
        state.ReportSucceeded(VoiceSubtitlesSettingsPhase.Mount);
        state.ReportSucceeded(VoiceSubtitlesSettingsPhase.Mount);

        Assert.Equal(1, capture.Count("event=voice_subtitles.settings.degraded"));
        Assert.Equal(1, capture.Count("event=voice_subtitles.settings.recovered"));
        Assert.Equal(2, capture.Count("phase=mount"));
        Assert.True(capture.Contains("reason_code=settings_apply_exception"));

        state.ReportDegraded(
            VoiceSubtitlesSettingsPhase.Show,
            new InvalidOperationException("new episode")
        );

        Assert.Equal(2, capture.Count("event=voice_subtitles.settings.degraded"));
    }

    [Fact]
    public void New_capture_does_not_receive_the_previous_captures_storm_summary()
    {
        BppLog.Flush();
        using (var producer = new LogCapture())
        {
            EmitPlaybackTrackingDegraded(attemptId: 1);
            EmitPlaybackTrackingDegraded(attemptId: 2);
            Assert.Equal(1, producer.Count("event=voice_subtitles.playback_tracking.degraded"));
        }

        using var consumer = new LogCapture();

        Assert.False(consumer.Contains("source_event=voice_subtitles.playback_tracking.degraded"));
    }

    private static void EmitPlaybackTrackingDegraded(long attemptId) =>
        BppLog.WarnEvent(
            VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegraded,
            VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedDisplayId.Bind(null),
            VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedAttemptId.Bind(attemptId),
            VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedReasonCode.Bind(
                VoiceSubtitlesLogReasonCode.PlaybackQueryException
            )
        );

    private static string DescribeFields(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceSubtitles.Tests");
        private readonly List<LogEventArgs> _events = new();

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal int Count(string text) =>
            _events.Count(entry =>
                entry.Data?.ToString()?.Contains(text, StringComparison.Ordinal) == true
            );

        internal bool Contains(string text) =>
            _events.Any(entry =>
                entry.Data?.ToString()?.Contains(text, StringComparison.Ordinal) == true
            );

        public void Dispose()
        {
            BppLog.Flush();
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args)
        {
            _events.Add(args);
        }
    }
}
