#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Input;

internal static class UpgradePreviewActivationSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "Shift Mode",
        "Shift 模式",
        "Shift-Modus",
        "Modo Shift",
        "Shift 모드",
        "Modalità Shift"
    );

    internal static CyclingSettingsDockEntry<HotkeyActivationMode> Create() =>
        new(
            BppSettingsDockOrder.UpgradePreviewActivation,
            "UpgradePreviewActivation",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            new[] { HotkeyActivationMode.Hold, HotkeyActivationMode.Toggle },
            config => config.UpgradePreviewActivationModeConfig.Value,
            (config, mode) =>
            {
                config.UpgradePreviewActivationModeConfig.Value = mode;
            },
            mode => mode == HotkeyActivationMode.Toggle,
            ResolveStatus
        );

    private static readonly LocalizedTextSet ToggleStatus = new("TOGGLE SHIFT", "按下 Shift 切换");

    private static readonly LocalizedTextSet HoldStatus = new("HOLD SHIFT", "按住 Shift");

    private static string ResolveStatus(HotkeyActivationMode mode, string languageCode) =>
        (mode == HotkeyActivationMode.Toggle ? ToggleStatus : HoldStatus).Resolve(
            languageCode,
            L.CurrentMode
        );
}
