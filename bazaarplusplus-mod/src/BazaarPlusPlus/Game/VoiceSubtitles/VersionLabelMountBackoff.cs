#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VersionLabelMountBackoff
{
    internal const float ScanIntervalSeconds = 0.75f;
    internal const float BackoffIntervalSeconds = 5f;
    internal const int MissesBeforeBackoff = 3;

    private int _consecutiveMisses;
    private float _nextAttemptAt;

    internal bool CanAttempt(float now) => now >= _nextAttemptAt;

    internal void RecordMiss(float now)
    {
        _consecutiveMisses++;
        _nextAttemptAt =
            now
            + (
                _consecutiveMisses >= MissesBeforeBackoff
                    ? BackoffIntervalSeconds
                    : ScanIntervalSeconds
            );
    }

    internal void Reset()
    {
        _consecutiveMisses = 0;
        _nextAttemptAt = 0f;
    }
}
