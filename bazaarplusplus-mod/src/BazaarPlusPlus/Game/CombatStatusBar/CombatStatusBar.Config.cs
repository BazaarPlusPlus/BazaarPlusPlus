#nullable enable

using BazaarPlusPlus.Infrastructure;

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
        CombatSpeedMultiplier = _services.Config.CombatStatusBarSpeedMultiplierConfig?.Value ?? 1f;
        BppLog.DebugEvent(
            CombatStatusBarLogEvents.ConfigLoaded,
            () =>
                [
                    CombatStatusBarLogEvents.ConfigLoadedEnabled.Bind(IsEnabled()),
                    CombatStatusBarLogEvents.ConfigLoadedSpeedMultiplier.Bind(
                        ToLogCategory(CombatSpeedMultiplier)
                    ),
                ]
        );
    }

    internal static bool IsEnabled()
    {
        return _services?.Config.EnableCombatStatusBarConfig?.Value ?? false;
    }

    internal static bool GetEnabledSettingValue()
    {
        return _services?.Config.EnableCombatStatusBarConfig?.Value ?? false;
    }

    internal static void SetEnabledSettingValue(bool enabled)
    {
        var config = _services?.Config.EnableCombatStatusBarConfig;
        if (config != null)
            config.Value = enabled;
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
