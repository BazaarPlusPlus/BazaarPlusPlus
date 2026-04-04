#nullable enable
using System;

namespace BazaarPlusPlus.Game.Upload;

internal enum StartupUploadAttemptDecision
{
    Wait,
    Start,
    SkipLiveRun,
    Done,
}

internal sealed class StartupUploadAttemptGate
{
    private const float DefaultRetryIntervalSeconds = 180f;
    private readonly float _retryIntervalSeconds;
    private float _eligibleAt;

    public StartupUploadAttemptGate(float startupDelaySeconds)
        : this(startupDelaySeconds, DefaultRetryIntervalSeconds) { }

    public StartupUploadAttemptGate(float startupDelaySeconds, float retryIntervalSeconds)
    {
        if (startupDelaySeconds < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(startupDelaySeconds),
                "Startup delay must be non-negative."
            );
        if (retryIntervalSeconds <= 0f)
            throw new ArgumentOutOfRangeException(
                nameof(retryIntervalSeconds),
                "Retry interval must be positive."
            );

        _eligibleAt = startupDelaySeconds;
        _retryIntervalSeconds = retryIntervalSeconds;
    }

    public void ArmImmediateAttempt(float currentTimeSeconds)
    {
        if (currentTimeSeconds < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(currentTimeSeconds),
                "Current time must be non-negative."
            );

        _eligibleAt = Math.Min(_eligibleAt, currentTimeSeconds);
    }

    public StartupUploadAttemptDecision Poll(float currentTimeSeconds, bool liveRunActive)
    {
        if (currentTimeSeconds < _eligibleAt)
            return StartupUploadAttemptDecision.Wait;

        if (liveRunActive)
            return StartupUploadAttemptDecision.SkipLiveRun;

        _eligibleAt = currentTimeSeconds + _retryIntervalSeconds;
        return StartupUploadAttemptDecision.Start;
    }
}
