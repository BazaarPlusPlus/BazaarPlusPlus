using BazaarPlusPlus.Game.CombatReplay;
using Xunit;

namespace BazaarPlusPlus.Tests.SavedReplays;

public sealed class SavedReplayReadTests
{
    private const string Battle = "battle-read";

    [Fact]
    public void Read_yields_to_host_and_reserves_all_replay_entries_until_playback_releases()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(Battle);
        using var context = new ReplayTestContext();
        using var read = new BlockedRead(h);
        var hostThread = Environment.CurrentManagedThreadId;
        var pending = h.Playback.ReplaySavedAsync(Battle, recordVideo: true);
        read.WaitForEntry();

        Assert.NotEqual(hostThread, read.ThreadId);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, h.Host.ScopeDepth);
        Assert.True(h.Playback.HasSavedReplaySession);
        Assert.True(h.Playback.IsReplayStartInProgress);
        Assert.False(h.LeaseIsFree());
        Assert.Null(h.Gate.TryAcquirePersistence(TimeSpan.Zero));
        Assert.False(context.Finish(h.Playback.ReplaySavedAsync(Battle, false)));
        Assert.False(h.Playback.CanReplaySavedCombats(out _));
        foreach (
            var source in new[]
            {
                CombatReplayPlaybackSource.ImportedGhost,
                CombatReplayPlaybackSource.LocalSaved,
            }
        )
            Assert.False(
                h.Playback.ReplayLoadedBattle(
                    SavedReplayPlaybackHarness.Manifest(Battle),
                    SavedReplayPlaybackHarness.Payload(Battle),
                    false,
                    source
                )
            );
        Assert.Empty(h.Timeline);
        read.Release();
        Assert.True(context.Finish(pending));
        Assert.Equal(hostThread, h.Host.BeginStartThread);
        Assert.False(h.LeaseIsFree());
        Assert.Contains($"starting:{Battle}:record=True", h.Timeline);
        h.Playback.OnReplayStateExited();
        Assert.True(h.LeaseIsFree());
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("run")]
    [InlineData("replay")]
    [InlineData("exit")]
    [InlineData("failure")]
    public void A_stale_or_failed_read_never_starts_or_rolls_back_a_game(string change)
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(Battle);
        using var context = new ReplayTestContext();
        using var read = new BlockedRead(h);
        using var cancellation = new CancellationTokenSource();
        var canCommit = true;
        var pending = h.Playback.ReplaySavedAsync(
            Battle,
            false,
            cancellation.Token,
            () => canCommit
        );
        read.WaitForEntry();
        switch (change)
        {
            case "cancel":
                cancellation.Cancel();
                break;
            case "selection":
                canCommit = false;
                break;
            case "run":
                h.Services.RunContext.IsInGameRun = true;
                break;
            case "replay":
                h.Host.InReplayState = true;
                break;
            case "exit":
                h.Playback.OnReplayStateExited();
                break;
            case "failure":
                read.Failure = new IOException("read failed");
                break;
        }
        var timelineBeforeReadSettles = h.Timeline.ToArray();
        read.Release();
        if (change is "cancel" or "selection")
            Assert.Throws<OperationCanceledException>(() => context.Finish(pending));
        else if (change == "failure")
            Assert.Throws<IOException>(() => context.Finish(pending));
        else
            Assert.False(context.Finish(pending));
        Assert.Equal(timelineBeforeReadSettles, h.Timeline);
        Assert.Null(h.Host.Start);
        Assert.Empty(h.Host.Notices);
        Assert.Empty(h.Terminals());
        Assert.False(h.Playback.HasSavedReplaySession);
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void Dispose_with_a_stopped_host_holds_the_gate_until_the_worker_finishes()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(Battle);
        using var context = new ReplayTestContext();
        using var read = new BlockedRead(h);
        var pending = h.Playback.ReplaySavedAsync(Battle, false);
        read.WaitForEntry();
        h.Playback.Dispose();
        context.Stop();
        Assert.False(h.LeaseIsFree());
        read.Release();
        // This waits on the real gate, without pumping the discarded UI continuation.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var maintenance = h.Gate.AcquireMaintenance(timeout.Token);
        Assert.False(pending.IsCompleted);
        Assert.Null(h.Host.Start);
        Assert.Empty(h.Timeline);
    }

    [Fact]
    public void Dispose_after_worker_completion_releases_without_running_queued_host_work()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(Battle);
        using var context = new ReplayTestContext();
        using var read = new BlockedRead(h);
        var pending = h.Playback.ReplaySavedAsync(Battle, false);
        read.WaitForEntry();
        read.Release();
        context.WaitForPost();
        Assert.False(h.LeaseIsFree());
        h.Playback.Dispose();
        context.Stop();
        Assert.True(h.LeaseIsFree());
        Assert.False(pending.IsCompleted);
        Assert.Empty(h.Timeline);
    }

    [Fact]
    public void Disposal_before_a_late_result_does_not_call_the_host_again()
    {
        using var h = new SavedReplayPlaybackHarness();
        h.Save(Battle);
        using var context = new ReplayTestContext();
        using var read = new BlockedRead(h);
        var pending = h.Playback.ReplaySavedAsync(Battle, false);
        read.WaitForEntry();
        h.Playback.Dispose();
        var calls = h.Host.ScopeEntries + h.Host.ReplayStateReads;
        read.Release();
        Assert.False(context.Finish(pending));
        Assert.Equal(calls, h.Host.ScopeEntries + h.Host.ReplayStateReads);
        Assert.Empty(h.Timeline);
        Assert.True(h.LeaseIsFree());
    }

    [Fact]
    public void An_already_canceled_request_does_not_schedule_a_read_or_leak_a_lease()
    {
        using var h = new SavedReplayPlaybackHarness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = h.Playback.ReplaySavedAsync(Battle, false, cancellation.Token);
        });
        Assert.Empty(h.CatalogReads);
        Assert.False(h.Playback.HasSavedReplaySession);
        Assert.True(h.LeaseIsFree());
    }

    private sealed class BlockedRead : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly ManualResetEventSlim _finished = new();
        public int ThreadId { get; private set; }
        public Exception? Failure { get; set; }

        public BlockedRead(SavedReplayPlaybackHarness harness)
        {
            harness.BeforeCatalogRead = () =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                _entered.Set();
                try
                {
                    if (!_release.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Test did not release its catalog read.");
                    if (Failure != null)
                        throw Failure;
                }
                finally
                {
                    _finished.Set();
                }
            };
        }

        public void WaitForEntry() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)));

        public void Release() => _release.Set();

        public void Dispose()
        {
            Release();
            if (_entered.IsSet)
                _finished.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
