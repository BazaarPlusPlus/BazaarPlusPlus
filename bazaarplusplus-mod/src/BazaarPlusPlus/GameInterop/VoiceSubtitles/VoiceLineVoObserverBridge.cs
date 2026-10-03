#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using TheBazaar.AppFramework;

namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal static class VoiceLineVoObserverBridge
{
    private static readonly HashSet<int> InstalledPlayers = new();
    private static int _nextAttemptId;
    private static volatile VoiceAttemptContext _pendingContext = VoiceAttemptContext.Unknown;
    private static volatile VoiceAttemptContext _activeContext = VoiceAttemptContext.Unknown;
    private static VoiceSubtitleObserverCallbacks _callbacks = VoiceSubtitleObserverCallbacks.Empty;

    internal static void Configure(VoiceSubtitleObserverCallbacks callbacks)
    {
        _callbacks = callbacks ?? VoiceSubtitleObserverCallbacks.Empty;
    }

    internal static void Install(VOPlayer? player)
    {
        if (player == null)
            return;

        var key = RuntimeHelpers.GetHashCode(player);
        if (!InstalledPlayers.Add(key))
            return;

        player.VODebugPrint -= OnVoDebugPrint;
        player.VODebugPrint += OnVoDebugPrint;
#if DEBUG
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.observer.installed"
            ),
            () => [("player_instance", PlayerInstance(player))]
        );
#endif
    }

    internal static void BeginVoiceAttempt(VoiceAttemptContext context)
    {
        _pendingContext = context;
#if DEBUG
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.attempt.started"),
            () =>
                [
                    ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                    ("origin", LogOrigin(context.Origin)),
                    ("player_instance", PlayerInstance(context.Player)),
                    ("source", LogSource(context.SourceLabel)),
                    ("hook", context.HookName),
                    ("event_ref", context.EventReferenceText),
                    ("event_path", context.EventPath),
                    ("event_duration_ms", Milliseconds(context.DurationSeconds)),
                ]
        );
#endif
    }

    internal static VoiceAttemptContext CreateVoiceAttempt(
        VOPlayer? player,
        bool isHero,
        CardAudio.AudioHookType audioHookType
    )
    {
        const string origin = "PlayVO";
        var sourceLabel = isHero ? "Hero" : "Merchant";
        var hookName = audioHookType.ToString();
        var eventRef = default(EventReference);

        try
        {
            var handler = Services.Get<SoundManager>().CardAudioHandler;
            var cardAudio =
                isHero ? handler.HeroCardAudio
                : audioHookType == CardAudio.AudioHookType.OnChoiceSelect ? handler.RewardCardAudio
                : handler.ActiveCardAudio;
            var hook = cardAudio?.GetAudioHook(audioHookType);
            if (hook != null)
                eventRef = hook.EventRef;
        }
        catch (Exception exception)
        {
            ReportObserverDegraded(
                VoiceObserverLogReasonCode.HookInspectionFailed,
                origin,
                hookName,
                eventReference: null,
                callbackEvent: null,
                exception
            );
        }

        return CreateVoiceAttempt(player, origin, sourceLabel, hookName, eventRef);
    }

    internal static VoiceAttemptContext CreateVoiceAttempt(
        VOPlayer? player,
        string origin,
        string sourceLabel,
        string hookName,
        EventReference eventRef
    )
    {
        var attemptId = Interlocked.Increment(ref _nextAttemptId);
        var normalizedOrigin = string.IsNullOrWhiteSpace(origin) ? "Unknown" : origin;
        var normalizedSource = string.IsNullOrWhiteSpace(sourceLabel) ? "Unknown" : sourceLabel;
        var normalizedHook = string.IsNullOrWhiteSpace(hookName) ? "Unknown" : hookName;
        var eventReferenceText = eventRef.IsNull ? null : eventRef.ToString();
        var eventPath = default(string);
        var durationSeconds = 0f;

        if (!eventRef.IsNull)
        {
            Exception? metadataException = null;
            var metadataUnavailable = false;
            try
            {
                var description = RuntimeManager.GetEventDescription(eventRef);
                if (description.getPath(out var path) == RESULT.OK)
                    eventPath = path;
                else
                    metadataUnavailable = true;

                if (description.getLength(out var lengthMs) == RESULT.OK && lengthMs > 0)
                    durationSeconds = lengthMs / 1000f;
                else
                    metadataUnavailable = true;
            }
            catch (Exception exception)
            {
                metadataUnavailable = true;
                metadataException = exception;
            }

            if (metadataUnavailable)
            {
                ReportObserverDegraded(
                    VoiceObserverLogReasonCode.EventMetadataUnavailable,
                    normalizedOrigin,
                    normalizedHook,
                    eventReferenceText,
                    callbackEvent: null,
                    metadataException
                );
            }
        }

        return new VoiceAttemptContext(
            attemptId,
            player,
            normalizedOrigin,
            normalizedSource,
            normalizedHook,
            eventReferenceText,
            eventPath,
            durationSeconds,
            Stopwatch.GetTimestamp()
        );
    }

    internal static void ClearVoiceAttempt(VoiceObserverLogReasonCode reasonCode)
    {
#if DEBUG
        var context = _pendingContext;
        if (context.IsKnown)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.attempt.cleared"
                ),
                () =>
                    [
                        ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                        ("reason_code", reasonCode),
                        ("age_ms", AgeMilliseconds(context)),
                    ]
            );
        }
#endif

        _pendingContext = VoiceAttemptContext.Unknown;
    }

    internal static void Reset()
    {
        InstalledPlayers.Clear();
        _pendingContext = VoiceAttemptContext.Unknown;
        _activeContext = VoiceAttemptContext.Unknown;
        _nextAttemptId = 0;
    }

    internal static void OnVoSoundPlayed(VOPlayer? player, IntPtr soundPtr)
    {
        if (!IsSubtitleObservationEnabled())
            return;

        var context = _activeContext;
        var pendingContext = _pendingContext;
        if (!context.IsKnown && pendingContext.IsKnown)
        {
            _activeContext = pendingContext;
            context = pendingContext;
        }

        if (!context.IsKnown)
        {
            ReportObserverDegraded(
                VoiceObserverLogReasonCode.AttemptContextUnavailable,
                context.Origin,
                context.HookName,
                context.EventReferenceText,
                callbackEvent: null,
                exception: null
            );
        }

        var sound = new Sound(soundPtr);
        var soundNameResult = ResolveSoundName(sound);
        if (soundNameResult.IsDegraded)
        {
            ReportObserverDegraded(
                VoiceObserverLogReasonCode.SoundNameUnavailable,
                context.Origin,
                context.HookName,
                context.EventReferenceText,
                callbackEvent: null,
                soundNameResult.Exception
            );
        }

        var soundDurationResult = ResolveSoundDurationSeconds(sound);
        if (soundDurationResult.IsDegraded)
        {
            ReportObserverDegraded(
                VoiceObserverLogReasonCode.SoundDurationUnavailable,
                context.Origin,
                context.HookName,
                context.EventReferenceText,
                callbackEvent: null,
                soundDurationResult.Exception
            );
        }

        var soundName = soundNameResult.Value;
        var soundDurationSeconds = soundDurationResult.Value;
        var sourceLabel = context.SourceLabel == "Unknown" ? "Hero" : context.SourceLabel;
        var hookName = context.HookName;
#if DEBUG
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.sound.observed"),
            () =>
                [
                    ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                    ("player_instance", PlayerInstance(player)),
                    ("context_player_instance", PlayerInstance(context.Player)),
                    ("source", LogSource(sourceLabel)),
                    ("hook", hookName),
                    ("sound_name", soundName),
                    ("sound_duration_ms", Milliseconds(soundDurationSeconds)),
                    ("event_path", context.EventPath),
                ]
        );
#endif

        var lookupText =
            $"{soundName ?? string.Empty} {context.EventPath ?? string.Empty} {context.EventReferenceText ?? string.Empty}";
        var lookup = ResolveLine(lookupText, sourceLabel, hookName, context.AttemptId);
        if (lookup.Failed)
            return;

        var resolution = lookup.Resolution;
        var line = resolution.Line;
        if (!HasResolvedLine(resolution))
        {
            LogLookupSkipped(context, resolution, soundName);
            return;
        }

        var durationSeconds =
            soundDurationSeconds > 0f ? soundDurationSeconds
            : context.DurationSeconds > 0f ? context.DurationSeconds
            : line.DurationSeconds;
        var cue = CreateCue(line, player ?? context.Player, durationSeconds, context.AttemptId);
        if (!QueueShow(cue))
            return;

        LogLookupResolved(context, resolution, soundDurationSeconds, durationSeconds);
    }

    internal static void OnVoPlaybackStopped(VOPlayer? player)
    {
#if DEBUG
        var context = _activeContext;
        if (context.IsKnown && IsSubtitleObservationEnabled())
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.attempt.stopped"
                ),
                () =>
                    [
                        ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                        ("player_instance", PlayerInstance(player)),
                        ("age_ms", AgeMilliseconds(context)),
                    ]
            );
        }
#endif

        _activeContext = VoiceAttemptContext.Unknown;
    }

    private static void OnVoDebugPrint(string eventReferenceText)
    {
        if (!IsSubtitleObservationEnabled())
            return;

        var context = _pendingContext;
        if (!context.IsKnown)
        {
            ReportObserverDegraded(
                VoiceObserverLogReasonCode.AttemptContextUnavailable,
                context.Origin,
                context.HookName,
                context.EventReferenceText,
                eventReferenceText,
                exception: null
            );
        }
        else
        {
            _activeContext = context;
        }

        var sourceLabel = context.SourceLabel == "Unknown" ? "Hero" : context.SourceLabel;
        var hookName = context.HookName;
#if DEBUG
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.callback.observed"),
            () =>
                [
                    ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                    ("origin", LogOrigin(context.Origin)),
                    ("age_ms", AgeMilliseconds(context)),
                    ("source", LogSource(sourceLabel)),
                    ("hook", hookName),
                    (
                        "context_matches_callback",
                        ContextMatchesCallback(context, eventReferenceText)
                    ),
                    ("callback_event", eventReferenceText),
                    ("context_event_ref", context.EventReferenceText),
                    ("context_event_path", context.EventPath),
                ]
        );
#endif

        if (string.Equals(context.Origin, "PlayVO", StringComparison.Ordinal))
            return;

        var lookupText =
            $"{context.EventPath ?? string.Empty} {context.EventReferenceText ?? string.Empty} {eventReferenceText}";
        var lookup = ResolveLine(lookupText, sourceLabel, hookName, context.AttemptId);
        if (lookup.Failed)
            return;

        var resolution = lookup.Resolution;
        var line = resolution.Line;
        if (!HasResolvedLine(resolution))
        {
            LogLookupSkipped(context, resolution, soundName: null);
            return;
        }

        var durationSeconds =
            context.DurationSeconds > 0f ? context.DurationSeconds : line.DurationSeconds;
        var cue = CreateCue(line, context.Player, durationSeconds, context.AttemptId);
        if (!QueueShow(cue))
            return;

        LogLookupResolved(context, resolution, soundDurationSeconds: 0f, durationSeconds);
    }

    private static VoiceSubtitlePlaybackCue CreateCue(
        VoiceSubtitleLine line,
        VOPlayer? player,
        float durationSeconds,
        int attemptId
    )
    {
        Func<bool>? isPlaybackStoppedOrStopping = null;
        Func<string>? playbackStateText = null;
        if (player != null)
        {
            isPlaybackStoppedOrStopping = () =>
            {
                var state = player.GetVOPlaybackState();
                return state == PLAYBACK_STATE.STOPPED || state == PLAYBACK_STATE.STOPPING;
            };
#if DEBUG
            playbackStateText = () => player.GetVOPlaybackState().ToString();
#endif
        }

        return new VoiceSubtitlePlaybackCue(
            line,
            durationSeconds,
            attemptId,
            isPlaybackStoppedOrStopping,
            playbackStateText
        );
    }

    private static RuntimeRead<string?> ResolveSoundName(Sound sound)
    {
        try
        {
            if (sound.getName(out var name, 512) == RESULT.OK && !string.IsNullOrWhiteSpace(name))
                return RuntimeRead<string?>.Success(name);
            return RuntimeRead<string?>.Degraded(null);
        }
        catch (Exception exception)
        {
            return RuntimeRead<string?>.Degraded(null, exception);
        }
    }

    private static RuntimeRead<float> ResolveSoundDurationSeconds(Sound sound)
    {
        try
        {
            if (sound.getLength(out var lengthMs, TIMEUNIT.MS) == RESULT.OK && lengthMs > 0)
                return RuntimeRead<float>.Success(lengthMs / 1000f);
            return RuntimeRead<float>.Degraded(0f);
        }
        catch (Exception exception)
        {
            return RuntimeRead<float>.Degraded(0f, exception);
        }
    }

    private static VoiceLineLookupOutcome ResolveLine(
        string lookupText,
        string sourceLabel,
        string hookName,
        int attemptId
    )
    {
        try
        {
            return VoiceLineLookupOutcome.Succeeded(
                _callbacks.ResolveLine(
                    new VoiceSubtitleLookupRequest(lookupText, sourceLabel, hookName)
                )
            );
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.lookup.failed"),
                exception,
                ("attempt_id", attemptId, BppLogCorrelationPolicy.Full),
                ("reason_code", VoiceObserverLogReasonCode.LookupCallbackFailed)
            );
            return VoiceLineLookupOutcome.Failure;
        }
    }

    internal static bool IsSubtitleObservationEnabled()
    {
        try
        {
            return _callbacks.IsEnabled();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.gate.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", VoiceObserverLogReasonCode.EnabledCheckFailed)
            );
            return false;
        }
    }

    private static bool QueueShow(VoiceSubtitlePlaybackCue cue)
    {
        try
        {
            _callbacks.QueueShow(cue);
            return true;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.display.failed"
                ),
                exception,
                ("display_id", null, BppLogCorrelationPolicy.Full),
                ("attempt_id", cue.AttemptId, BppLogCorrelationPolicy.Full),
                ("stem", cue.Line.Stem),
                ("reason_code", VoiceSubtitleDisplayLogReasonCode.QueueFailed)
            );
            return false;
        }
    }

    private static void ReportObserverDegraded(
        VoiceObserverLogReasonCode reasonCode,
        string? origin,
        string? hook,
        string? eventReference,
        string? callbackEvent,
        Exception? exception
    )
    {
        var fields = new BppLogField[]
        {
            ("reason_code", reasonCode),
            ("origin", LogOrigin(origin)),
            ("hook", string.IsNullOrWhiteSpace(hook) ? "Unknown" : hook),
            ("event_ref", eventReference),
            ("callback_event", callbackEvent),
        };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.observer.degraded",
                    storm: ["reason_code", "hook"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.observer.degraded",
                    storm: ["reason_code", "hook"]
                ),
                exception,
                fields
            );
    }

    private static void LogLookupSkipped(
        VoiceAttemptContext context,
        VoiceSubtitleLookupResult resolution,
        string? soundName
    )
    {
#if DEBUG
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.lookup.skipped"),
            () =>
                [
                    ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                    ("origin", LogOrigin(context.Origin)),
                    ("strategy", resolution.Strategy),
                    ("hook", context.HookName),
                    ("sound_name", soundName),
                    ("reason_code", VoiceObserverLogReasonCode.NoMatch),
                ]
        );
#endif
    }

    private static void LogLookupResolved(
        VoiceAttemptContext context,
        VoiceSubtitleLookupResult resolution,
        float soundDurationSeconds,
        float displayDurationSeconds
    )
    {
#if DEBUG
        var line = resolution.Line;
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.lookup.resolved"),
            () =>
                [
                    ("attempt_id", context.AttemptId, BppLogCorrelationPolicy.Full),
                    ("origin", LogOrigin(context.Origin)),
                    ("strategy", resolution.Strategy),
                    ("catalog", resolution.CatalogName),
                    ("matched_token", resolution.MatchedToken),
                    ("candidate_count", resolution.CandidateCount),
                    ("stem", line.Stem),
                    ("sound_duration_ms", Milliseconds(soundDurationSeconds)),
                    ("event_duration_ms", Milliseconds(context.DurationSeconds)),
                    ("line_duration_ms", Milliseconds(line.DurationSeconds)),
                    ("display_duration_ms", Milliseconds(displayDurationSeconds)),
                    ("english_text", line.English),
                    ("chinese_text", line.Chinese),
                ]
        );
#endif
    }

    private static bool HasResolvedLine(VoiceSubtitleLookupResult resolution)
    {
        return resolution.HasLine && !string.IsNullOrEmpty(resolution.Line.Stem);
    }

    private static long AgeMilliseconds(VoiceAttemptContext context)
    {
        if (!context.IsKnown)
            return 0L;

        var elapsedTicks = Stopwatch.GetTimestamp() - context.CreatedAtTimestamp;
        return Math.Max(
            0L,
            (long)
                Math.Round(
                    elapsedTicks * 1000d / Stopwatch.Frequency,
                    MidpointRounding.AwayFromZero
                )
        );
    }

    private static long Milliseconds(float seconds) =>
        Math.Max(0L, (long)Math.Round(seconds * 1000d, MidpointRounding.AwayFromZero));

    private static string? PlayerInstance(VOPlayer? player)
    {
        if (player == null)
            return null;

        return player.GetType().Name
            + "#"
            + RuntimeHelpers.GetHashCode(player).ToString("X8", CultureInfo.InvariantCulture);
    }

    private static VoiceObserverLogOrigin LogOrigin(string? origin)
    {
        if (string.Equals(origin, "PlayVO", StringComparison.Ordinal))
            return VoiceObserverLogOrigin.PlayVo;
        if (string.Equals(origin, "PlayTutorialVO", StringComparison.Ordinal))
            return VoiceObserverLogOrigin.PlayTutorialVo;
        return VoiceObserverLogOrigin.Unknown;
    }

    private static VoiceObserverLogSource LogSource(string? source)
    {
        if (string.Equals(source, "Hero", StringComparison.Ordinal))
            return VoiceObserverLogSource.Hero;
        if (string.Equals(source, "Merchant", StringComparison.Ordinal))
            return VoiceObserverLogSource.Merchant;
        return VoiceObserverLogSource.Unknown;
    }

    private static bool ContextMatchesCallback(
        VoiceAttemptContext context,
        string eventReferenceText
    )
    {
        if (!context.IsKnown || string.IsNullOrWhiteSpace(eventReferenceText))
            return false;

        return ContainsEither(eventReferenceText, context.EventReferenceText)
            || ContainsEither(eventReferenceText, context.EventPath);
    }

    private static bool ContainsEither(string callbackText, string? contextText)
    {
        if (string.IsNullOrWhiteSpace(contextText))
            return false;

        return callbackText.IndexOf(contextText!, StringComparison.OrdinalIgnoreCase) >= 0
            || contextText!.IndexOf(callbackText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private readonly struct RuntimeRead<T>
    {
        private RuntimeRead(T value, bool isDegraded, Exception? exception)
        {
            Value = value;
            IsDegraded = isDegraded;
            Exception = exception;
        }

        internal T Value { get; }

        internal bool IsDegraded { get; }

        internal Exception? Exception { get; }

        internal static RuntimeRead<T> Success(T value) => new(value, false, null);

        internal static RuntimeRead<T> Degraded(T value, Exception? exception = null) =>
            new(value, true, exception);
    }

    private readonly struct VoiceLineLookupOutcome
    {
        internal static readonly VoiceLineLookupOutcome Failure = new(
            VoiceSubtitleLookupResult.Empty,
            failed: true
        );

        private VoiceLineLookupOutcome(VoiceSubtitleLookupResult resolution, bool failed)
        {
            Resolution = resolution;
            Failed = failed;
        }

        internal VoiceSubtitleLookupResult Resolution { get; }

        internal bool Failed { get; }

        internal static VoiceLineLookupOutcome Succeeded(VoiceSubtitleLookupResult resolution) =>
            new(resolution, failed: false);
    }

    internal sealed class VoiceAttemptContext
    {
        public static readonly VoiceAttemptContext Unknown = new(
            0,
            null,
            "Unknown",
            "Unknown",
            "Unknown",
            null,
            null,
            0f,
            0L
        );

        public VoiceAttemptContext(
            int attemptId,
            VOPlayer? player,
            string origin,
            string sourceLabel,
            string hookName,
            string? eventReferenceText,
            string? eventPath,
            float durationSeconds,
            long createdAtTimestamp
        )
        {
            AttemptId = attemptId;
            Player = player;
            Origin = origin;
            SourceLabel = sourceLabel;
            HookName = hookName;
            EventReferenceText = eventReferenceText;
            EventPath = eventPath;
            DurationSeconds = durationSeconds;
            CreatedAtTimestamp = createdAtTimestamp;
        }

        public int AttemptId { get; }

        public VOPlayer? Player { get; }

        public string Origin { get; }

        public string SourceLabel { get; }

        public string HookName { get; }

        public string? EventReferenceText { get; }

        public string? EventPath { get; }

        public float DurationSeconds { get; }

        public long CreatedAtTimestamp { get; }

        public bool IsKnown => AttemptId > 0;
    }
}
