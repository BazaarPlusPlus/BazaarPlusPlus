#nullable enable
namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockLayoutSyncTracker
{
    private static readonly float[] DelayedProbeSeconds = [0.25f, 0.5f, 1f, 2f, 4f, 8f];
    private const float SteadyStateProbeIntervalSeconds = 2f;

    private readonly int _immediateSyncFrameCount;
    private string? _lastSceneName;
    private int _pendingImmediateSyncFrames;
    private int _nextDelayedProbe;
    private float _sceneChangedAtSeconds;
    private float _nextSteadyStateProbeAtSeconds;

    internal BppDockLayoutSyncTracker(int immediateSyncFrameCount)
    {
        _immediateSyncFrameCount = immediateSyncFrameCount > 0 ? immediateSyncFrameCount : 1;
    }

    internal bool ShouldSync(string sceneName, float realtimeSeconds)
    {
        if (!string.Equals(sceneName, _lastSceneName, StringComparison.Ordinal))
        {
            _lastSceneName = sceneName;
            _pendingImmediateSyncFrames = _immediateSyncFrameCount;
            _nextDelayedProbe = 0;
            _sceneChangedAtSeconds = realtimeSeconds;
            _nextSteadyStateProbeAtSeconds =
                realtimeSeconds + DelayedProbeSeconds[^1] + SteadyStateProbeIntervalSeconds;
        }

        if (_pendingImmediateSyncFrames > 0)
        {
            _pendingImmediateSyncFrames--;
            return true;
        }

        var shouldProbe = false;
        while (
            _nextDelayedProbe < DelayedProbeSeconds.Length
            && realtimeSeconds >= _sceneChangedAtSeconds + DelayedProbeSeconds[_nextDelayedProbe]
        )
        {
            _nextDelayedProbe++;
            shouldProbe = true;
        }

        if (
            _nextDelayedProbe >= DelayedProbeSeconds.Length
            && realtimeSeconds >= _nextSteadyStateProbeAtSeconds
        )
        {
            do _nextSteadyStateProbeAtSeconds += SteadyStateProbeIntervalSeconds;
            while (realtimeSeconds >= _nextSteadyStateProbeAtSeconds);

            shouldProbe = true;
        }

        return shouldProbe;
    }
}
