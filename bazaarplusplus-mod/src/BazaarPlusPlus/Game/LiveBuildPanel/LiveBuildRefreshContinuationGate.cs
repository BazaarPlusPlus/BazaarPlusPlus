#nullable enable
namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildRefreshContinuationGate
{
    private int _version;

    internal int Capture() => Volatile.Read(ref _version);

    internal bool IsCurrent(int version) => version == Volatile.Read(ref _version);

    internal void Invalidate() => Interlocked.Increment(ref _version);
}
