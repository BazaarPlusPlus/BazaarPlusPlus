namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// A touch inspection's open and close timing, shared by both hosts: readiness and interruptible
/// easing. The view owns transforms and rental cleanup.
/// </summary>
public sealed class InspectionTransition
{
    private float _elapsed;
    private float _exitFrom;

    public float Progress { get; private set; }
    public bool Animated { get; private set; }
    public bool Pending { get; private set; }
    public bool Closing { get; private set; }
    public bool Finished { get; private set; } = true;

    public void Begin(bool animated)
    {
        Animated = animated;
        Progress = 0f;
        _elapsed = 0f;
        Pending = true;
        Closing = Finished = false;
    }

    public void Ready()
    {
        if (!Pending || Finished)
            return;
        Pending = false;
        if (!Animated)
            Progress = 1f;
    }

    public void Dismiss()
    {
        if (Closing || Finished)
            return;
        Closing = true;
        _elapsed = 0f;
        _exitFrom = Progress;
        if (Pending || !Animated || Progress <= 0f)
            Cancel();
    }

    public void Tick(float seconds)
    {
        if (Pending || Finished)
            return;
        _elapsed += Math.Max(0f, seconds);
        var t = Math.Min(1f, _elapsed / (Closing ? 0.20f : 0.32f));
        // Start and settle at rest; long travel should not launch at its highest speed.
        var eased = Math.Clamp(t * t * t * (10f + t * (-15f + 6f * t)), 0f, 1f);
        Progress =
            Closing ? _exitFrom * (1f - eased)
            : Animated ? eased
            : 1f;
        if (Closing && t >= 1f)
            Cancel();
    }

    public void Cancel()
    {
        Progress = 0f;
        Pending = false;
        Finished = true;
    }
}
