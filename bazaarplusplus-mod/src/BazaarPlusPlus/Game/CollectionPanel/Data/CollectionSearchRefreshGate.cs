#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionSearchRefreshGate
{
    private readonly float _delaySeconds;
    private float _remainingSeconds;

    public CollectionSearchRefreshGate(float delaySeconds)
    {
        if (delaySeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));

        _delaySeconds = delaySeconds;
    }

    public bool IsPending { get; private set; }

    public void Schedule()
    {
        _remainingSeconds = _delaySeconds;
        IsPending = true;
    }

    public void Cancel()
    {
        _remainingSeconds = 0f;
        IsPending = false;
    }

    public bool Advance(float deltaSeconds, bool isComposing = false)
    {
        if (!IsPending)
            return false;
        if (isComposing)
            return false;

        _remainingSeconds -= Math.Max(0f, deltaSeconds);
        if (_remainingSeconds > 0f)
            return false;

        Cancel();
        return true;
    }
}
