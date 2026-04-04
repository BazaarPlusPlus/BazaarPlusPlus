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

        _configStateInitialized = true;
        CombatSpeedMultiplier =
            BppRuntimeHost.Config.CombatStatusBarSpeedMultiplierConfig?.Value ?? 1f;
        BppLog.Info(
            "CombatStatusBar",
            $"Combat config initialized: enabled={IsEnabled()}, speed={CombatSpeedMultiplier:F2}x"
        );
    }

    internal static bool IsEnabled()
    {
        return BppRuntimeHost.Config.EnableCombatStatusBarConfig?.Value ?? false;
    }

    internal static bool GetEnabledSettingValue()
    {
        return BppRuntimeHost.Config.EnableCombatStatusBarConfig?.Value ?? false;
    }

    internal static void SetEnabledSettingValue(bool enabled)
    {
        var config = BppRuntimeHost.Config.EnableCombatStatusBarConfig;
        if (config != null)
            config.Value = enabled;
    }

    static partial void PersistCombatSpeed(float speed)
    {
        var config = BppRuntimeHost.Config.CombatStatusBarSpeedMultiplierConfig;
        if (config != null)
            config.Value = speed;
    }
}
