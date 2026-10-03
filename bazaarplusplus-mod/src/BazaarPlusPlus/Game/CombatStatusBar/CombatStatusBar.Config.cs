#nullable enable

using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    private static bool _configStateInitialized;

    internal static void EnsureConfigStateInitialized()
    {
        if (_configStateInitialized)
            return;

        if (_services == null)
            return;

        _configStateInitialized = true;
        CombatSpeedMultiplier = NormalizeConfiguredDefaultSpeed(
            _services.Config.CombatStatusBarSpeedMultiplierConfig.Value
        );
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.CombatStatusBar, "combat_status_bar.config.loaded"),
            () => [("speed_multiplier", ToLogCategory(CombatSpeedMultiplier))]
        );
    }

    static partial void PersistCombatSpeed(float speed)
    {
        var config = _services?.Config.CombatStatusBarSpeedMultiplierConfig;
        if (config != null)
            config.Value = speed;
    }

    private static CombatSpeedLogCategory ToLogCategory(float speed)
    {
        if (System.Math.Abs(speed - 0.5f) < 0.001f)
            return CombatSpeedLogCategory.Half;
        if (System.Math.Abs(speed - 0.67f) < 0.001f)
            return CombatSpeedLogCategory.TwoThirds;
        if (System.Math.Abs(speed - 1f) < 0.001f)
            return CombatSpeedLogCategory.Normal;
        return CombatSpeedLogCategory.Custom;
    }
}
