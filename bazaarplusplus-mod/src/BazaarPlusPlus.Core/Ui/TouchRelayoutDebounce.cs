#nullable enable

namespace BazaarPlusPlus.Core.Ui;

// System bars and rotation can report transient screen keys, so wait for them to settle before re-renting cards.
// Bound the wait for native setup so a hung operation cannot prevent relayout.
public sealed class TouchRelayoutDebounce
{
    public const int DefaultStableFrames = 10;

    public const int DefaultMaxBusyFrames = 120;

    private string? _candidate;
    private int _seen;
    private int _busy;

    public TouchRelayoutDebounce(
        string laidOut,
        int stableFrames = DefaultStableFrames,
        int maxBusyFrames = DefaultMaxBusyFrames
    )
    {
        if (stableFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(stableFrames));
        LaidOut = laidOut ?? throw new ArgumentNullException(nameof(laidOut));
        StableFrames = stableFrames;
        MaxBusyFrames = Math.Max(0, maxBusyFrames);
    }

    public int StableFrames { get; }

    public int MaxBusyFrames { get; }

    public string LaidOut { get; private set; }

    public long HeldFrames { get; private set; }

    public void LaidOutAt(string key)
    {
        LaidOut = key ?? throw new ArgumentNullException(nameof(key));
        _candidate = null;
        _seen = 0;
        _busy = 0;
    }

    public bool Observe(string key, bool settingUp)
    {
        if (string.Equals(key, LaidOut, StringComparison.Ordinal))
        {
            _candidate = null;
            _seen = 0;
            _busy = 0;
            return false;
        }
        if (!string.Equals(key, _candidate, StringComparison.Ordinal))
        {
            _candidate = key;
            _seen = 0;
            _busy = 0;
        }
        _seen++;
        if (_seen < StableFrames)
        {
            HeldFrames++;
            return false;
        }
        if (settingUp && _busy < MaxBusyFrames)
        {
            _busy++;
            HeldFrames++;
            return false;
        }
        return true;
    }
}
