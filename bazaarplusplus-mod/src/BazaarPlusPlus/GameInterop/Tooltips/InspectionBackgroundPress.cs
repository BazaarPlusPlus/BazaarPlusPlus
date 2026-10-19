#nullable enable
namespace BazaarPlusPlus.GameInterop.Tooltips;

internal sealed class InspectionBackgroundPress
{
    private int _lastFrame = -1;
    private bool _armed;

    public bool Sample(int frame, bool pressed, bool released, bool insideControls)
    {
        if (frame == _lastFrame)
            return false;
        _lastFrame = frame;
        // Both edges in one sample cannot recover the original down position.
        if (pressed && released)
        {
            Reset();
            return false;
        }
        if (pressed)
        {
            _armed = !insideControls;
        }
        if (!released)
            return false;
        var dismiss = _armed && !insideControls;
        Reset();
        return dismiss;
    }

    public void Reset()
    {
        _armed = false;
    }
}
