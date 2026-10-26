#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

/// <summary>When <see cref="VersionLabelScanner"/> may run a full label scan or retry a mount
/// from the cached label path. A miss (no label found, or a mount that failed) waits one scan
/// interval, and from the third consecutive miss on the backoff interval; a successful mount
/// or the feature switching off resets it. It never stops retrying.</summary>
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
