#nullable enable

using System.Reflection;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Patches.VoiceSubtitles;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceSubtitlesEventCatalogTests
{
    private static readonly Type[] EventSources =
    [
        typeof(VoiceCatalogLogEvents),
        typeof(VoiceSubtitlesDisplayLogEvents),
        typeof(VoiceObserverLogEvents),
        typeof(VoiceSubtitleDisplayLogEvents),
        typeof(VoicePatchLogEvents),
    ];

    [Fact]
    public void Event_catalog_has_exactly_34_unique_voice_subtitle_definitions()
    {
        var definitions = EventSources.SelectMany(Definitions).ToArray();

        Assert.Equal(34, definitions.Length);
        Assert.Equal(
            34,
            definitions
                .Select(definition => definition.EventId)
                .Distinct(StringComparer.Ordinal)
                .Count()
        );
        Assert.All(
            definitions,
            definition => Assert.Equal(BppLogFeatureScope.VoiceSubtitles, definition.Scope)
        );
    }

    [Fact]
    public void Observer_patch_and_shared_display_schemas_match_the_locked_manifest()
    {
        var actual = new[]
        {
            typeof(VoiceObserverLogEvents),
            typeof(VoiceSubtitleDisplayLogEvents),
            typeof(VoicePatchLogEvents),
        }
            .SelectMany(Definitions)
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voice_subtitles.observer.installed"] = "player_instance:High:None",
            ["voice_subtitles.attempt.started"] =
                "attempt_id:High:Full|origin:Low:None|player_instance:High:None|source:Low:None|hook:Low:None|event_ref:High:None|event_path:High:None|event_duration_ms:High:None",
            ["voice_subtitles.observer.degraded"] =
                "reason_code:Low:None|origin:Low:None|hook:Low:None|event_ref:High:None|callback_event:High:None",
            ["voice_subtitles.attempt.cleared"] =
                "attempt_id:High:Full|reason_code:Low:None|age_ms:High:None",
            ["voice_subtitles.sound.observed"] =
                "attempt_id:High:Full|player_instance:High:None|context_player_instance:High:None|source:Low:None|hook:Low:None|sound_name:High:None|sound_duration_ms:High:None|event_path:High:None",
            ["voice_subtitles.lookup.skipped"] =
                "attempt_id:High:Full|origin:Low:None|strategy:Low:None|hook:Low:None|sound_name:High:None|reason_code:Low:None",
            ["voice_subtitles.lookup.resolved"] =
                "attempt_id:High:Full|origin:Low:None|strategy:Low:None|catalog:Low:None|matched_token:High:None|candidate_count:High:None|stem:High:None|sound_duration_ms:High:None|event_duration_ms:High:None|line_duration_ms:High:None|display_duration_ms:High:None|english_text:High:None|chinese_text:High:None",
            ["voice_subtitles.attempt.stopped"] =
                "attempt_id:High:Full|player_instance:High:None|age_ms:High:None",
            ["voice_subtitles.callback.observed"] =
                "attempt_id:High:Full|origin:Low:None|age_ms:High:None|source:Low:None|hook:Low:None|context_matches_callback:Low:None|callback_event:High:None|context_event_ref:High:None|context_event_path:High:None",
            ["voice_subtitles.lookup.failed"] = "attempt_id:High:Full|reason_code:Low:None",
            ["voice_subtitles.gate.degraded"] = "reason_code:Low:None",
            ["voice_subtitles.display.failed"] =
                "display_id:High:Full|attempt_id:High:Full|stem:High:None|reason_code:Low:None",
            ["voice_subtitles.observer.failed"] = "reason_code:Low:None",
            ["voice_subtitles.callback_patch.degraded"] =
                "reason_code:Low:None|actual_count:High:None|expected_count:Low:None",
            ["voice_subtitles.callback_patch.ready"] =
                "actual_count:High:None|expected_count:Low:None",
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Debug_field_factory_is_not_evaluated_in_release()
    {
        using var capture = new LogCapture();
        var evaluated = false;

        BppLog.DebugEvent(
            VoiceCatalogLogEvents.CatalogStarted,
            () =>
            {
                evaluated = true;
                return [];
            }
        );

#if DEBUG
        Assert.True(evaluated);
        Assert.Contains("event=voice_subtitles.catalog.started", capture.Joined);
#else
        Assert.False(evaluated);
        Assert.DoesNotContain("event=voice_subtitles.catalog.started", capture.Joined);
#endif
    }

    private static IEnumerable<BppLogEventDefinition> Definitions(Type source) =>
        source
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string DescribeFields(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceSubtitlesEventCatalog.Tests");
        private readonly List<string> _events = [];

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal string Joined => string.Join("\n", _events);

        public void Dispose()
        {
            BppLog.Flush();
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args)
        {
            _events.Add(args.Data?.ToString() ?? string.Empty);
        }
    }
}
