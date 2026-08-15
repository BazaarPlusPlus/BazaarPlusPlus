#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum CapturedReplayRoute
{
    CurrentNative,
    PersistedPvp,
}

internal static class CapturedReplayRouter
{
    internal static CapturedReplayRoute Resolve(PvpBattleManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        return manifest.CombatKind switch
        {
            "Combat" => CapturedReplayRoute.CurrentNative,
            "PVPCombat" => CapturedReplayRoute.PersistedPvp,
            _ => throw new ArgumentException(
                $"Unsupported replay combat kind: {manifest.CombatKind ?? "<null>"}.",
                nameof(manifest)
            ),
        };
    }
}
