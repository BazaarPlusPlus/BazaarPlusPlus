#nullable enable

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed class PortraitCachedSetupState(long generation)
{
    internal long Generation { get; } = generation;
    internal bool AwaitingNative { get; private set; } = true;
    internal bool HoldsStatic { get; private set; } = true;

    internal bool Commit(long generation)
    {
        if (Generation != generation)
            return false;
        AwaitingNative = false;
        return true;
    }

    // Transfer and cancellation both revoke ownership. An old async request can never
    // release the same hold twice after a newer setup has acquired the renderer.
    internal bool ReleaseStatic()
    {
        if (!HoldsStatic)
            return false;
        HoldsStatic = false;
        return true;
    }
}
