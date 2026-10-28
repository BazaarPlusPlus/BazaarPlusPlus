using BazaarPlusPlus.Game.CombatReplay.Video;
using Xunit;

namespace PureBehavior.Tests;

public sealed class NativeFrameSubmissionTests
{
    [Fact]
    public void SuccessfulTicksPackFramesWithoutPaddingHitches()
    {
        var submission = new NativeFrameSubmission(10);
        var target = new FrameTarget();

        submission.Capture(0, target);
        submission.Capture(0.05, target);
        Assert.Single(target.Frames);
        submission.Capture(0.21, target);

        Assert.Equal([(0L, 1), (1L, 2)], target.Frames);
        Assert.Equal(2, submission.CapturedFrames);
        Assert.Equal(1, submission.RepeatedFrames);
        Assert.Equal(3, submission.OutputFrameIndex);
        Assert.True(submission.FirstFrameTimestamp > 0);
        var firstFrame = submission.FirstFrameTimestamp;

        submission.Capture(10, target);

        Assert.Equal((3L, 3), target.Frames[^1]);
        Assert.Equal(6, submission.OutputFrameIndex);
        Assert.Equal(3, submission.CapturedFrames);
        Assert.Equal(3, submission.RepeatedFrames);
        Assert.True(submission.PacerResyncDroppedFrames > 0);
        Assert.Equal(submission.PacerResyncDroppedFrames, submission.DroppedFrames);
        Assert.Equal(firstFrame, submission.FirstFrameTimestamp);
        Assert.Equal(EventState.Committed, target.State);
        Assert.False(submission.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableFrameAdvancesTimeWithoutRepayingItOnTheNextTick(bool rejected)
    {
        var submission = new NativeFrameSubmission(10);
        var target = new FrameTarget
        {
            Preparation = rejected
                ? NativeFramePreparation.Rejected
                : NativeFramePreparation.LeaseUnavailable,
        };

        submission.Capture(0, target);

        Assert.Equal(1, submission.OutputFrameIndex);
        Assert.Equal(1, submission.DroppedFrames);
        Assert.Equal(rejected ? 0 : 1, submission.LeaseMisses);
        Assert.Equal(rejected ? 0 : 1, submission.BackpressureDroppedFrames);
        Assert.Equal(rejected ? 1 : 0, submission.EnqueueRejects);
        Assert.Equal(0, submission.FirstFrameTimestamp);
        Assert.False(submission.Failed);
        Assert.Equal(EventState.Absent, target.State);

        target.Preparation = NativeFramePreparation.Ready;
        submission.Capture(0.11, target);

        Assert.Equal([(1L, 1)], target.Frames);
        Assert.Equal(2, submission.OutputFrameIndex);
        Assert.Equal(1, submission.CapturedFrames);
        Assert.Equal(0, submission.RepeatedFrames);
        Assert.Equal(1, submission.DroppedFrames);
    }

    [Theory]
    [InlineData(FailurePoint.Queue, EventState.Discarded)]
    [InlineData(FailurePoint.Execute, EventState.Cancelled)]
    public void SubmissionFailureSettlesTheEventAccordingToWhetherItWasQueued(
        FailurePoint failure,
        EventState expected
    )
    {
        var submission = new NativeFrameSubmission(10);
        var target = new FrameTarget();
        submission.Capture(0, target);
        target.Failure = failure;

        submission.Capture(10, target);

        Assert.Equal(expected, target.State);
        Assert.Equal(4, submission.OutputFrameIndex);
        Assert.Equal(1, submission.CapturedFrames);
        Assert.Equal(0, submission.RepeatedFrames);
        Assert.Equal(3 + submission.PacerResyncDroppedFrames, submission.DroppedFrames);
        Assert.True(submission.Failed);
        Assert.Same(target.Error, submission.FailureException);
    }

    [Theory]
    [InlineData(FailurePoint.Clear)]
    [InlineData(FailurePoint.Prepare)]
    public void PreparationExceptionsKeepTheirExistingPropagation(FailurePoint failure)
    {
        var submission = new NativeFrameSubmission(10);
        var target = new FrameTarget { Failure = failure };

        Assert.Same(
            target.Error,
            Assert.Throws<InvalidOperationException>(() => submission.Capture(0, target))
        );

        Assert.Equal(EventState.Absent, target.State);
        Assert.Equal(0, submission.OutputFrameIndex);
        Assert.Equal(0, submission.DroppedFrames);
        Assert.False(submission.Failed);
    }

    [Fact]
    public void MissingCommandsFailTheCaptureAndCountTheLostFrame()
    {
        var submission = new NativeFrameSubmission(10);

        submission.Capture(0, null);

        Assert.True(submission.Failed);
        Assert.Null(submission.FailureException);
        Assert.Equal(1, submission.DroppedFrames);
        Assert.Equal(1, submission.OutputFrameIndex);
        Assert.Equal(0, submission.FirstFrameTimestamp);
    }

    public enum FailurePoint
    {
        None,
        Clear,
        Prepare,
        Queue,
        Execute,
    }

    public enum EventState
    {
        Absent,
        Prepared,
        Queued,
        Committed,
        Cancelled,
        Discarded,
    }

    private sealed class FrameTarget : INativeFrameTarget
    {
        internal NativeFramePreparation Preparation { get; set; } = NativeFramePreparation.Ready;
        internal FailurePoint Failure { get; set; }
        internal EventState State { get; private set; }
        internal List<(long FirstFrame, int Count)> Frames { get; } = [];
        internal InvalidOperationException Error { get; } = new("render effect failed");

        public void Clear() => ThrowAt(FailurePoint.Clear);

        public NativeFramePreparation Prepare(
            long firstFrameIndex,
            int frameCount,
            out IntPtr eventData
        )
        {
            ThrowAt(FailurePoint.Prepare);
            eventData = IntPtr.Zero;
            if (Preparation == NativeFramePreparation.Ready)
            {
                Frames.Add((firstFrameIndex, frameCount));
                eventData = new IntPtr(Frames.Count);
                State = EventState.Prepared;
            }
            return Preparation;
        }

        public void Queue(IntPtr eventData)
        {
            Assert.Equal(EventState.Prepared, State);
            ThrowAt(FailurePoint.Queue);
            State = EventState.Queued;
        }

        public void ExecuteAndCommit(IntPtr eventData)
        {
            Assert.Equal(EventState.Queued, State);
            ThrowAt(FailurePoint.Execute);
            State = EventState.Committed;
        }

        public void Cancel(IntPtr eventData)
        {
            Assert.Equal(EventState.Queued, State);
            State = EventState.Cancelled;
        }

        public void Discard(IntPtr eventData)
        {
            Assert.Equal(EventState.Prepared, State);
            State = EventState.Discarded;
        }

        private void ThrowAt(FailurePoint point)
        {
            if (Failure == point)
                throw Error;
        }
    }
}
