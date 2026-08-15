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
            config =>
                config.UpgradePreviewActivationModeConfig?.Value
                ?? BppConfig.DefaultUpgradePreviewActivationMode,
            (config, mode) =>
            {
                var entry = config.UpgradePreviewActivationModeConfig;
                if (entry != null)
                    entry.Value = mode;
            },
            mode => mode == HotkeyActivationMode.Toggle,
            ResolveStatus
        );

    private static string ResolveStatus(HotkeyActivationMode mode, string languageCode)
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return mode == HotkeyActivationMode.Toggle ? "按下 Shift 切换" : "按住 Shift";

        return mode == HotkeyActivationMode.Toggle ? "TOGGLE SHIFT" : "HOLD SHIFT";
    }
}
