#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel;

// Decides when a still inspection may skip re-measuring the native description. A change
// marks the layout dirty; it settles once the motion is done and two frames measure the same
// description bounds, already centred on the target. A settled layout is re-measured every
// RecheckFrames as a backstop, so a change no dirty source reported stays stale at most that long.
internal sealed class CollectionInspectionLayoutSettle
{
    public const int RecheckFrames = 30;
    public const float Epsilon = .5f;

    private bool _dirty = true;
    private bool _changed = true;
    private int _settledFrame;
    private int _measuredFrame = -1;
    private int _measuredFrames;
    private (float X, float Y, float Width, float Height)? _last;

    // Frames measured in the current or most recent dirty episode.
    public int MeasuredFrames => _measuredFrames;

    public void MarkDirty()
    {
        _dirty = true;
        _changed = true;
        _last = null;
        _measuredFrame = -1;
        _measuredFrames = 0;
    }

    public bool ShouldMeasure(int frame)
    {
        if (!_dirty && frame - _settledFrame >= RecheckFrames)
        {
            _dirty = true;
            _measuredFrames = 0;
        }
        return _dirty;
    }

    // Returns true only when this frame settles a layout that changed since it last settled;
    // a periodic recheck that finds nothing new settles silently.
    public bool Observe(
        int frame,
        float x,
        float y,
        float width,
        float height,
        float targetX,
        float targetY,
        bool motionDone
    )
    {
        if (!_dirty)
            return false;
        var next = (x, y, width, height);
        if (frame == _measuredFrame)
        {
            _last = next;
            return false;
        }
        _measuredFrame = frame;
        _measuredFrames++;
        var same =
            _last is { } last
            && Math.Abs(last.X - x) <= Epsilon
            && Math.Abs(last.Y - y) <= Epsilon
            && Math.Abs(last.Width - width) <= Epsilon
            && Math.Abs(last.Height - height) <= Epsilon;
        _last = next;
        if (!same)
            _changed = true;
        var inPlace = Math.Abs(targetX - x) <= Epsilon && Math.Abs(targetY - y) <= Epsilon;
        if (!motionDone || !same || !inPlace)
            return false;
        _dirty = false;
        _settledFrame = frame;
        var changed = _changed;
        _changed = false;
        return changed;
    }
}
