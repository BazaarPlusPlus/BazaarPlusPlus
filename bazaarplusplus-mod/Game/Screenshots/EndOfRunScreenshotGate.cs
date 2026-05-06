#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotGate
{
    private bool _attemptInFlight;
    private bool _capturedForCurrentRun;
    private bool _allowNextContinuePassthrough;
    private float _retryAvailableAtSeconds;

    public bool ShouldCaptureOnContinue(bool isInteractionBlocked, float nowSeconds)
    {
        if (
            isInteractionBlocked
            || _capturedForCurrentRun
            || _attemptInFlight
            || nowSeconds < _retryAvailableAtSeconds
        )
        {
            return false;
        }

        _attemptInFlight = true;
        return true;
    }

    public void MarkAttemptCompleted()
    {
        _capturedForCurrentRun = true;
    }

    public void CompleteCaptureAttempt()
    {
        MarkAttemptCompleted();
    }

    public void AbortCaptureAttempt(float retryAvailableAtSeconds)
    {
        _attemptInFlight = false;
        _retryAvailableAtSeconds = retryAvailableAtSeconds;
    }

    public void CancelCaptureAttempt()
    {
        _attemptInFlight = false;
    }

    public void AllowNextContinuePassthrough()
    {
        _allowNextContinuePassthrough = true;
    }

    public bool ConsumeContinuePassthrough()
    {
        if (!_allowNextContinuePassthrough)
            return false;

        _allowNextContinuePassthrough = false;
        _attemptInFlight = false;
        return true;
    }

    public bool IsAttemptInFlight()
    {
        return _attemptInFlight;
    }

    public bool HasCapturedForCurrentRun()
    {
        return _capturedForCurrentRun;
    }

    public void ResetForNewRun()
    {
        _attemptInFlight = false;
        _capturedForCurrentRun = false;
        _allowNextContinuePassthrough = false;
        _retryAvailableAtSeconds = 0f;
    }
}
