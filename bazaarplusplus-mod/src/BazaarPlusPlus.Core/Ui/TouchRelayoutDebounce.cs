#nullable enable

namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// When an open touch panel lays itself out again for a changed screen. A safe area that flickers
/// while the system bars settle, or a rotation that reports a few sizes in turn, would otherwise
/// rebuild the panel, and rent its cards again, once per frame: the change waits until the
/// screen key reads the same for <see cref="StableFrames"/> frames in a row. It also waits while
/// the panel still has a card setting up, so a rebuild never lands in the middle of one, but
/// never longer than <see cref="MaxBusyFrames"/>, so a setup that never ends cannot keep a
/// panel laid out for the wrong screen. Unity-free; the host feeds it one key per frame.
/// </summary>
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

    /// <summary>The screen key the panel is laid out for.</summary>
    public string LaidOut { get; private set; }

    /// <summary>Frames a different key was seen but not yet acted on, over the panel's life.</summary>
    public long HeldFrames { get; private set; }

    /// <summary>The panel was laid out for <paramref name="key"/>, by a relayout or on opening.</summary>
    public void LaidOutAt(string key)
    {
        LaidOut = key ?? throw new ArgumentNullException(nameof(key));
        _candidate = null;
        _seen = 0;
        _busy = 0;
    }

    /// <summary>
    /// One frame's screen key; <paramref name="settingUp"/> is whether the panel has a card in
    /// setup. True when the panel should lay itself out for <paramref name="key"/> now.
    /// </summary>
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
