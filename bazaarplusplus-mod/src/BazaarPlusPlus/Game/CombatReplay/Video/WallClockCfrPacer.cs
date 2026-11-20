#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

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

    // now is monotonic wall-clock time in seconds.
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
            if (!hasFrame)
                break;

            emit++;
            if (latestSeq != lastEmittedSeq)
                lastEmittedSeq = latestSeq;
            else
                repeat++;

            _nextEmitTime += _frameInterval;
        }

        // A resync bounds catch-up so a renderer stall cannot leave the pacer permanently behind wall time.
        if (now - _nextEmitTime > _frameInterval * _resyncOvershootIntervals)
        {
            dropped += (int)Math.Floor((now - _nextEmitTime) / _frameInterval);
            _nextEmitTime = now + _frameInterval;
        }

        return new CfrTickResult(emit, repeat, dropped);
    }

    public void Reset()
    {
        _hasStarted = false;
        _nextEmitTime = 0;
    }
}
