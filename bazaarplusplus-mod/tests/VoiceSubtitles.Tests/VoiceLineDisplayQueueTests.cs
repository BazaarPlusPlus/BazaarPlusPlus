using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using BepInEx.Logging;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VoiceLineDisplayQueueTests
{
    [Fact]
    public void Queued_cue_is_retained_until_the_renderer_is_mounted()
    {
        var shown = new List<VoiceSubtitleCue>();
        VoiceLineDisplay.Reset();
        try
        {
            VoiceLineDisplay.EnqueueQueuedShow(CreateCue(1, () => false));

            VoiceLineDisplay.ProcessQueuedShows(isMounted: false, shown.Add);
            Assert.Empty(shown);

            VoiceLineDisplay.ProcessQueuedShows(isMounted: true, shown.Add);
            Assert.Equal(1, Assert.Single(shown).AttemptId);
        }
        finally
        {
            VoiceLineDisplay.Reset();
        }
    }

    [Fact]
    public void Stopped_cue_is_evicted_before_the_renderer_mounts()
    {
        var stopped = false;
        var shown = new List<VoiceSubtitleCue>();
        VoiceLineDisplay.Reset();
        try
        {
            VoiceLineDisplay.EnqueueQueuedShow(CreateCue(1, () => stopped));
            VoiceLineDisplay.ProcessQueuedShows(isMounted: false, shown.Add);

            stopped = true;
            VoiceLineDisplay.ProcessQueuedShows(isMounted: false, shown.Add);
            VoiceLineDisplay.ProcessQueuedShows(isMounted: true, shown.Add);

            Assert.Empty(shown);
        }
        finally
        {
            VoiceLineDisplay.Reset();
        }
    }

    [Fact]
    public void Pre_mount_queue_is_bounded_to_the_latest_cues()
    {
        var shown = new List<VoiceSubtitleCue>();
        VoiceLineDisplay.Reset();
        try
        {
            for (var attemptId = 1; attemptId <= VoiceLineDisplay.MaxQueuedShows + 2; attemptId++)
                VoiceLineDisplay.EnqueueQueuedShow(CreateCue(attemptId, () => false));

            VoiceLineDisplay.ProcessQueuedShows(isMounted: true, shown.Add);

            Assert.Equal(VoiceLineDisplay.MaxQueuedShows, shown.Count);
            Assert.Equal(3, shown[0].AttemptId);
            Assert.Equal(VoiceLineDisplay.MaxQueuedShows + 2, shown[^1].AttemptId);
        }
        finally
        {
            VoiceLineDisplay.Reset();
        }
    }

    [Fact]
    public void Playback_query_failure_is_logged_once_and_the_untrackable_cue_is_evicted()
    {
        using var capture = new LogCapture();
        var shown = new List<VoiceSubtitleCue>();
        VoiceLineDisplay.Reset();
        try
        {
            VoiceLineDisplay.EnqueueQueuedShow(
                CreateCue(1, () => throw new InvalidOperationException("first query failed"))
            );
            VoiceLineDisplay.EnqueueQueuedShow(
                CreateCue(2, () => throw new InvalidOperationException("second query failed"))
            );

            VoiceLineDisplay.ProcessQueuedShows(isMounted: false, shown.Add);
            VoiceLineDisplay.ProcessQueuedShows(isMounted: true, shown.Add);

            Assert.Empty(shown);
            Assert.Equal(1, capture.Count("event=voice_subtitles.playback_tracking.degraded"));
            Assert.True(capture.Contains("display_id=null"));
            Assert.True(capture.Contains("reason_code=playback_query_exception"));
        }
        finally
        {
            VoiceLineDisplay.Reset();
        }
    }

    private static VoiceSubtitleCue CreateCue(int attemptId, Func<bool> isStopped) =>
        new(
            new VoiceLine($"stem-{attemptId}", "English", "中文", 1f),
            eventDurationSeconds: 1f,
            attemptId,
            isStopped,
            playbackStateText: null
        );

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("VoiceLineDisplayQueueTests");
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
