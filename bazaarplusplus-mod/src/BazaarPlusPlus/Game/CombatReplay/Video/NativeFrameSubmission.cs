#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum NativeFramePreparation
{
    Ready,
    LeaseUnavailable,
    Rejected,
}

/// <summary>One platform's render-thread submission effects, private to frame submission.</summary>
internal interface INativeFrameTarget
{
    void Clear();
    NativeFramePreparation Prepare(long firstFrameIndex, int frameCount, out IntPtr eventData);
    void Queue(IntPtr eventData);
    void ExecuteAndCommit(IntPtr eventData);
    void Cancel(IntPtr eventData);
    void Discard(IntPtr eventData);
}

/// <summary>
/// Owns the per-tick submission protocol and its accounting. Pacer resync slots never extend
/// the output timeline; a frame lost during native submission still advances it.
/// </summary>
internal sealed class NativeFrameSubmission(int fps)
{
    private readonly WallClockCfrPacer _pacer = new(fps);
    private int _issuedSequence;
    private long _lastEmittedSequence;

    internal int CapturedFrames { get; private set; }
    internal int DroppedFrames { get; private set; }
    internal int RepeatedFrames { get; private set; }
    internal int LeaseMisses { get; private set; }
    internal int BackpressureDroppedFrames { get; private set; }
    internal int EnqueueRejects { get; private set; }
    internal int PacerResyncDroppedFrames { get; private set; }
    internal long OutputFrameIndex { get; private set; }
    internal long FirstFrameTimestamp { get; private set; }
    internal bool Failed { get; private set; }
    internal Exception? FailureException { get; private set; }
    internal ReplayVideoCopyTimingAccumulator CopyTiming { get; } = new();

    internal void Capture(double now, INativeFrameTarget? target)
    {
        var sequence = Interlocked.Increment(ref _issuedSequence);
        var tick = _pacer.Tick(now, true, sequence, ref _lastEmittedSequence);
        var plan = NativeFrameSubmissionPlan.Create(
            tick.EmitCount,
            tick.RepeatCount,
            tick.DroppedCount
        );
        PacerResyncDroppedFrames += plan.PacerDroppedFrameCount;
        if (plan.EncodeFrameCount <= 0)
        {
            DroppedFrames += plan.PacerDroppedFrameCount;
            return;
        }

        if (target == null)
        {
            Drop(plan);
            Failed = true;
            return;
        }

        // These effects intentionally remain outside the submission catch. In particular,
        // preparing a frame has not queued a render event we could cancel or discard here.
        target.Clear();
        var copyStarted = Stopwatch.GetTimestamp();
        var prepared = target.Prepare(OutputFrameIndex, plan.EncodeFrameCount, out var eventData);
        if (prepared == NativeFramePreparation.LeaseUnavailable)
        {
            LeaseMisses++;
            BackpressureDroppedFrames += plan.EncodeFrameCount;
            Drop(plan);
            return;
        }
        if (prepared == NativeFramePreparation.Rejected)
        {
            EnqueueRejects++;
            Drop(plan);
            return;
        }

        OutputFrameIndex += plan.TimelineFrameCount;
        var eventQueued = false;
        try
        {
            target.Queue(eventData);
            eventQueued = true;
            target.ExecuteAndCommit(eventData);
            CapturedFrames += plan.CapturedFrameCount;
            if (FirstFrameTimestamp == 0 && CapturedFrames > 0)
                FirstFrameTimestamp = Stopwatch.GetTimestamp();
            RepeatedFrames += plan.RepeatedFrameCount;
            DroppedFrames += plan.PacerDroppedFrameCount;
        }
        catch (Exception ex)
        {
            if (eventQueued)
                target.Cancel(eventData);
            else
                target.Discard(eventData);
            DroppedFrames += plan.DroppedFrameCountOnSubmissionFailure;
            Failed = true;
            FailureException ??= ex;
        }
        finally
        {
            CopyTiming.ObserveSince(copyStarted);
        }
    }

    private void Drop(NativeFrameSubmissionPlan plan)
    {
        DroppedFrames += plan.DroppedFrameCountOnSubmissionFailure;
        OutputFrameIndex += plan.TimelineFrameCount;
    }
}
