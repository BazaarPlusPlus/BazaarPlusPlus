using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPresentationTaskTrackerTests
{
    [Fact]
    public void TrackBeforePresentationIsVisible_RemainsPendingUntilTaskCompletes()
    {
        var tracker = new ReplayPresentationTaskTracker();
        using var scope = tracker.BeginTracking();
        var completion = new TaskCompletionSource<bool>();

        tracker.Track(completion.Task);

        Assert.Equal([completion.Task], tracker.SnapshotPending());

        completion.SetResult(true);

        Assert.Empty(tracker.SnapshotPending());
    }

    [Fact]
    public void TrackAfterFirstQuietSample_IsIncludedInNextSample()
    {
        var tracker = new ReplayPresentationTaskTracker();
        using var scope = tracker.BeginTracking();

        Assert.Empty(tracker.SnapshotPending());

        var completion = new TaskCompletionSource<bool>();
        tracker.Track(completion.Task);

        Assert.Equal([completion.Task], tracker.SnapshotPending());
    }

    [Fact]
    public void Dispose_ClearsPendingTasksAndAllowsNextReplayScope()
    {
        var tracker = new ReplayPresentationTaskTracker();
        var firstScope = tracker.BeginTracking();
        tracker.Track(new TaskCompletionSource<bool>().Task);

        firstScope.Dispose();

        using var secondScope = tracker.BeginTracking();
        Assert.Empty(tracker.SnapshotPending());
    }

    [Fact]
    public void FaultedTask_RemainsVisibleToWaiter()
    {
        var tracker = new ReplayPresentationTaskTracker();
        using var scope = tracker.BeginTracking();
        var failure = Task.FromException(new InvalidOperationException("spawn failed"));

        tracker.Track(failure);

        Assert.Equal([failure], tracker.SnapshotPending());
    }
}
