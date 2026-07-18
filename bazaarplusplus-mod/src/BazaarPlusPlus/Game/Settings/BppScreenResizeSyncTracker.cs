#nullable enable

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppScreenResizeSyncTracker
{
    private readonly int _syncFrameCount;
    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;
    private int _pendingSyncFrames;

    internal BppScreenResizeSyncTracker(int syncFrameCount)
    {
        _syncFrameCount = syncFrameCount > 0 ? syncFrameCount : 1;
    }

    internal bool ShouldSync(int currentWidth, int currentHeight)
    {
        if (_lastScreenWidth != currentWidth || _lastScreenHeight != currentHeight)
        {
            _lastScreenWidth = currentWidth;
            _lastScreenHeight = currentHeight;
            _pendingSyncFrames = _syncFrameCount;
        }

        if (_pendingSyncFrames <= 0)
            return false;

        _pendingSyncFrames--;
        return true;
    }
}
