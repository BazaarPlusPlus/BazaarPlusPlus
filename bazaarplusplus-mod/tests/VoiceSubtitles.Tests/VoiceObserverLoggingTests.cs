#nullable enable

using System.Reflection;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Patches.VoiceSubtitles;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VoiceObserverLoggingCollection
{
    public const string Name = "Voice observer logging";
}

[Collection(VoiceObserverLoggingCollection.Name)]
public sealed class VoiceObserverLoggingTests
{
    [Fact]
    public void Observer_patch_and_shared_display_catalogs_match_manifest()
    {
        Assert.Equal(
            [
                "voice_subtitles.attempt.cleared",
                "voice_subtitles.attempt.started",
                "voice_subtitles.attempt.stopped",
                "voice_subtitles.callback.observed",
                "voice_subtitles.gate.degraded",
                "voice_subtitles.lookup.failed",
                "voice_subtitles.lookup.resolved",
                "voice_subtitles.lookup.skipped",
                "voice_subtitles.observer.degraded",
                "voice_subtitles.observer.installed",
                "voice_subtitles.sound.observed",
            ],
            EventIds(typeof(VoiceObserverLogEvents))
        );
        Assert.Equal(
            [
                "voice_subtitles.callback_patch.degraded",
                "voice_subtitles.callback_patch.ready",
                "voice_subtitles.observer.failed",
            ],
            EventIds(typeof(VoicePatchLogEvents))
        );
        Assert.Equal(
            ["voice_subtitles.display.failed"],
            EventIds(typeof(VoiceSubtitleDisplayLogEvents))
        );
    }

    [Fact]
    public void Shared_display_failure_schema_uses_full_workflow_correlations()
    {
        var definition = VoiceSubtitleDisplayLogEvents.DisplayFailed;

        Assert.Equal(
            ["display_id", "attempt_id", "stem", "reason_code"],
            definition.Fields.Select(field => field.Name)
        );
        Assert.Equal(
            BppLogCorrelationPolicy.Full,
            VoiceSubtitleDisplayLogEvents.DisplayId.Correlation
        );
        Assert.Equal(
            BppLogCorrelationPolicy.Full,
            VoiceSubtitleDisplayLogEvents.AttemptId.Correlation
        );
        Assert.Null(definition.StormPolicy);
    }

    [Fact]
    public void Observer_degradation_key_is_reason_and_hook_only()
    {
        var definition = VoiceObserverLogEvents.ObserverDegraded;

        Assert.Equal(
            ["reason_code", "hook"],
            definition.StormPolicy!.KeyFields.Select(field => field.Name)
        );
    }

    [Fact]
    public void Enabled_gate_fallback_warns_once_for_repeated_callback_failures()
    {
        using var capture = new LogCapture();
        BppLog.RecoverStorm(
            VoiceObserverLogEvents.GateDegraded,
            VoiceObserverLogEvents.GateDegradedReasonCode.Bind(
                VoiceObserverLogReasonCode.EnabledCheckFailed
            )
        );
        ConfigureObserver(
            new VoiceSubtitleObserverCallbacks(
                _ => VoiceSubtitleLookupResult.Empty,
                () => throw new InvalidOperationException("gate failed"),
                _ => { }
            )
        );

        try
        {
            Assert.False(IsObservationEnabled());
            Assert.False(IsObservationEnabled());

            Assert.Equal(1, capture.Count("event=voice_subtitles.gate.degraded"));
            Assert.Contains("reason_code=enabled_check_failed", capture.Joined);
        }
        finally
        {
            ConfigureObserver(VoiceSubtitleObserverCallbacks.Empty);
            BppLog.RecoverStorm(
                VoiceObserverLogEvents.GateDegraded,
                VoiceObserverLogEvents.GateDegradedReasonCode.Bind(
                    VoiceObserverLogReasonCode.EnabledCheckFailed
                )
            );
        }
    }

    [Fact]
    public void Lookup_callback_failure_emits_one_terminal_error_without_skip()
    {
        using var capture = new LogCapture();
        ResetObserver();
        ConfigureObserver(
            new VoiceSubtitleObserverCallbacks(
                _ => throw new InvalidOperationException("lookup exploded"),
                () => true,
                _ => throw new InvalidOperationException("queue must not run")
            )
        );

        try
        {
            var context = CreateTutorialAttempt();
            BeginAttempt(context);

            InvokeDebugCallback("event:/VO/Tutorial/line");

            Assert.Equal(1, capture.Count("event=voice_subtitles.lookup.failed"));
            Assert.Equal(0, capture.Count("event=voice_subtitles.lookup.skipped"));
            Assert.Contains($"attempt_id={AttemptId(context)}", capture.Joined);
            Assert.Contains("reason_code=lookup_callback_failed", capture.Joined);
        }
        finally
        {
            ConfigureObserver(VoiceSubtitleObserverCallbacks.Empty);
            ResetObserver();
        }
    }

    [Fact]
    public void Queue_callback_failure_emits_shared_terminal_error_without_resolved_result()
    {
        using var capture = new LogCapture();
        ResetObserver();
        ConfigureObserver(
            new VoiceSubtitleObserverCallbacks(
                _ => new VoiceSubtitleLookupResult(
                    new VoiceSubtitleLine("safe_stem", "English", "中文", 1.25f),
                    hasLine: true,
                    strategy: "event-stem",
                    matchedToken: "safe_stem",
                    catalogName: "test"
                ),
                () => true,
                _ => throw new InvalidOperationException("queue exploded")
            )
        );

        try
        {
            var context = CreateTutorialAttempt();
            BeginAttempt(context);

            InvokeDebugCallback("event:/VO/Tutorial/safe_stem");

            Assert.Equal(1, capture.Count("event=voice_subtitles.display.failed"));
            Assert.Equal(0, capture.Count("event=voice_subtitles.lookup.resolved"));
            Assert.Contains("display_id=null", capture.Joined);
            Assert.Contains($"attempt_id={AttemptId(context)}", capture.Joined);
            Assert.Contains("stem=safe_stem", capture.Joined);
            Assert.Contains("reason_code=queue_failed", capture.Joined);
        }
        finally
        {
            ConfigureObserver(VoiceSubtitleObserverCallbacks.Empty);
            ResetObserver();
        }
    }

    [Fact]
    public void Observer_and_patch_sources_do_not_bypass_structured_privacy_contract()
    {
        foreach (
            var relativePath in new[]
            {
                Path.Combine(
                    "src",
                    "BazaarPlusPlus",
                    "GameInterop",
                    "VoiceSubtitles",
                    "VoiceLineVoObserverBridge.cs"
                ),
                Path.Combine(
                    "src",
                    "BazaarPlusPlus",
                    "Patches",
                    "VoiceSubtitles",
                    "VOPlayerPatches.cs"
                ),
            }
        )
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
            Assert.DoesNotContain("VoiceSubtitlesLog", source, StringComparison.Ordinal);
            Assert.DoesNotContain("VoiceSubtitlesInteropLog", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Message", source, StringComparison.Ordinal);
            Assert.DoesNotContain("soundPtr=", source, StringComparison.Ordinal);
        }
    }

    private static string[] EventIds(Type sourceType) =>
        sourceType
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => ((BppLogEventDefinition)field.GetValue(null)!).EventId)
            .OrderBy(eventId => eventId, StringComparer.Ordinal)
            .ToArray();

    private static void InvokeDebugCallback(string eventReferenceText)
    {
        var callback =
            ObserverType().GetMethod("OnVoDebugPrint", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing VO debug callback.");
        callback.Invoke(null, [eventReferenceText]);
    }

    private static object CreateTutorialAttempt()
    {
        var create = ObserverType()
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method =>
                method.Name == "CreateVoiceAttempt" && method.GetParameters().Length == 5
            );
        var eventReferenceType = create.GetParameters()[4].ParameterType;
        return create.Invoke(
                null,
                [
                    null,
                    "PlayTutorialVO",
                    "Hero",
                    "Tutorial",
                    Activator.CreateInstance(eventReferenceType),
                ]
            ) ?? throw new InvalidOperationException("Attempt creation returned null.");
    }

    private static void BeginAttempt(object context) =>
        ObserverType()
            .GetMethod("BeginVoiceAttempt", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [context]);

    private static int AttemptId(object context) =>
        Assert.IsType<int>(context.GetType().GetProperty("AttemptId")!.GetValue(context));

    private static void ConfigureObserver(VoiceSubtitleObserverCallbacks callbacks) =>
        ObserverType()
            .GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [callbacks]);

    private static void ResetObserver() =>
        ObserverType()
            .GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null);

    private static bool IsObservationEnabled() =>
        Assert.IsType<bool>(
            ObserverType()
                .GetMethod(
                    "IsSubtitleObservationEnabled",
                    BindingFlags.Static | BindingFlags.NonPublic
                )!
                .Invoke(null, null)
        );

    private static Type ObserverType() =>
        typeof(VoiceSubtitleObserverCallbacks).Assembly.GetType(
            "BazaarPlusPlus.GameInterop.VoiceSubtitles.VoiceLineVoObserverBridge",
            throwOnError: true
        )!;

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceObserverLogging.Tests");
        private readonly List<string> _events = [];

        internal LogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal string Joined => string.Join("\n", _events);

        internal int Count(string text) =>
            _events.Count(entry => entry.Contains(text, StringComparison.Ordinal));

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
