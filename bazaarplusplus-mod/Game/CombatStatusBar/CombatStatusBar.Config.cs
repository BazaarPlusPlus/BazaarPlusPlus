#nullable enable

using BazaarPlusPlus.Core.Runtime;

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
        BppLog.Info(
            "CombatStatusBar",
            $"Combat config initialized: enabled={IsEnabled()}, speed={CombatSpeedMultiplier:F2}x"
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
}
