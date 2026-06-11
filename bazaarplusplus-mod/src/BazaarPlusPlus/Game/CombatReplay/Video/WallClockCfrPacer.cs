#nullable enable
using System;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Pure, deterministic constant-frame-rate pacer for the replay video capture
/// session. Capture (ScreenCapture + AsyncGPUReadback) updates a "latest frame"
/// staging slot at whatever rhythm the GPU allows; this pacer is driven by an
/// injected wall-clock value (seconds) and decides, per tick, how many CFR slots
/// elapsed and whether each emitted slot is a brand-new source frame or a repeat
/// of the previous one. It also counts capture slots lost to a catch-up resync.
///
/// The pacer holds no Unity, FMOD, or game references and performs no I/O, so it
/// is fully unit-testable. The caller owns the actual Rent/copy/enqueue work and
/// the captured/dropped/repeated counters; the pacer only decides the counts.
/// </summary>
internal sealed class WallClockCfrPacer
{
    private readonly double _frameInterval;
    private readonly int _maxEmitsPerTick;
    private readonly int _resyncOvershootIntervals;
    private double _nextEmitTime;
    private bool _hasStarted;

    public WallClockCfrPacer(int fps, int maxEmitsPerTick = 3, int resyncOvershootIntervals = 5)
    {
        _frameInterval = 1.0 / Math.Max(1, fps);
        _maxEmitsPerTick = maxEmitsPerTick;
        _resyncOvershootIntervals = resyncOvershootIntervals;
    }

    /// <summary>
    /// Result of a single <see cref="Tick"/>.
    /// <para><see cref="EmitCount"/> = total enqueue attempts this tick (new + repeat);
    /// the number of times the caller should Rent+copy-staging+enqueue the latest frame.</para>
    /// <para><see cref="RepeatCount"/> = subset of <see cref="EmitCount"/> that re-emit the
    /// last source sequence (caller adds these to its repeated-frames counter, not captured).</para>
    /// <para><see cref="DroppedCount"/> = capture slots skipped by overshoot resync this tick.</para>
    /// </summary>
    internal readonly struct CfrTickResult
    {
        public readonly int EmitCount;
        public readonly int RepeatCount;
        public readonly int DroppedCount;

        public CfrTickResult(int emitCount, int repeatCount, int droppedCount)
        {
            EmitCount = emitCount;
            RepeatCount = repeatCount;
            DroppedCount = droppedCount;
        }
    }

    /// <summary>
    /// Advance the CFR clock to <paramref name="now"/> and decide how many frames to emit.
    /// </summary>
    /// <param name="now">Monotonic wall-clock value in seconds (injected; never read internally).</param>
    /// <param name="hasFrame">Whether a staging frame is currently available to emit.</param>
    /// <param name="latestSeq">Sequence id of the latest captured source frame.</param>
    /// <param name="lastEmittedSeq">
    /// The last source sequence the caller emitted; updated in place when a new source
    /// frame is emitted so the next tick can distinguish new frames from repeats.
    /// </param>
    public CfrTickResult Tick(double now, bool hasFrame, long latestSeq, ref long lastEmittedSeq)
    {
        if (!_hasStarted)
        {
            // Do not pad leading blanks: the timeline starts at the first frame.
            if (!hasFrame)
                return new CfrTickResult(0, 0, 0);

            _nextEmitTime = now;
            _hasStarted = true;
        }

        var emit = 0;
        var repeat = 0;
        var dropped = 0;

        while (now >= _nextEmitTime && emit < _maxEmitsPerTick)
        {
            // No staging frame yet: advance the clock via the overshoot resync below
            // rather than emitting; bail out of the per-tick emit loop.
            if (!hasFrame)
                break;

            emit++;
            if (latestSeq != lastEmittedSeq)
                lastEmittedSeq = latestSeq;
            else
                repeat++;

            _nextEmitTime += _frameInterval;
        }

        // If we have fallen far enough behind real time (more than
        // resyncOvershootIntervals slots), snap forward so the video stays
        // wall-clock aligned. The skipped slots are counted as dropped captures.
        if (now - _nextEmitTime > _frameInterval * _resyncOvershootIntervals)
        {
            dropped += (int)Math.Floor((now - _nextEmitTime) / _frameInterval);
            _nextEmitTime = now + _frameInterval;
        }

        return new CfrTickResult(emit, repeat, dropped);
    }

    /// <summary>
    /// Reset the pacer so a finalized session instance can be reused for a fresh recording.
    /// </summary>
    public void Reset()
    {
        _hasStarted = false;
        _nextEmitTime = 0;
    }
}
