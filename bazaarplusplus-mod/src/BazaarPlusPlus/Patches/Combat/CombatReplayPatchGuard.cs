#nullable enable
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Patches.Combat;

internal static class CombatReplayPatchGuard
{
    internal static bool IsReplayStartOrPlaybackActive
    {
        get
        {
            var runtime = CombatReplayRuntime.Instance;
            return runtime?.IsReplayStartInProgress == true
                || runtime?.IsSavedReplayPlaybackActive == true;
        }
    }
}
