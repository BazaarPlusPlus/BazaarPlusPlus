#nullable enable

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunVisualStabilityTracker
{
    internal const float RequiredStableSeconds = 0.5f;

    private bool _hasBaseline;
    private bool _motionObserved;
    private int _loadedCardCount;
    private ulong _cardSetFingerprint;
    private ulong _poseFingerprint;
    private float _lastMotionAtSeconds;

    public bool Observe(
        int loadedCardCount,
        ulong cardSetFingerprint,
        ulong poseFingerprint,
        float nowSeconds
    )
    {
        if (loadedCardCount <= 0 || float.IsNaN(nowSeconds) || float.IsInfinity(nowSeconds))
        {
            Reset();
            return false;
        }

        if (
            !_hasBaseline
            || loadedCardCount != _loadedCardCount
            || cardSetFingerprint != _cardSetFingerprint
            || nowSeconds < _lastMotionAtSeconds
        )
        {
            SetBaseline(loadedCardCount, cardSetFingerprint, poseFingerprint, nowSeconds);
            return false;
        }

        if (poseFingerprint != _poseFingerprint)
        {
            _poseFingerprint = poseFingerprint;
            _lastMotionAtSeconds = nowSeconds;
            _motionObserved = true;
            return false;
        }

        return _motionObserved && nowSeconds - _lastMotionAtSeconds >= RequiredStableSeconds;
    }

    public void Reset()
    {
        _hasBaseline = false;
        _motionObserved = false;
        _loadedCardCount = 0;
        _cardSetFingerprint = 0;
        _poseFingerprint = 0;
        _lastMotionAtSeconds = 0f;
    }

    private void SetBaseline(
        int loadedCardCount,
        ulong cardSetFingerprint,
        ulong poseFingerprint,
        float nowSeconds
    )
    {
        _hasBaseline = true;
        _motionObserved = false;
        _loadedCardCount = loadedCardCount;
        _cardSetFingerprint = cardSetFingerprint;
        _poseFingerprint = poseFingerprint;
        _lastMotionAtSeconds = nowSeconds;
    }
}
