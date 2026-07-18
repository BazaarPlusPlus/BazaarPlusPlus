#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class ItemEnchantPreviewSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<PreviewVisibilityMode> Create() =>
        new(
            BppSettingsDockOrder.EnchantPreview,
            "EnchantPreview",
            EnchantPreviewSettingsMenuLabel.Resolve,
            new[]
            {
                PreviewVisibilityMode.Off,
                PreviewVisibilityMode.AutoOnPedestalChoice,
                PreviewVisibilityMode.Always,
            },
            config => config.EnchantPreviewModeConfig?.Value ?? BppConfig.DefaultEnchantPreviewMode,
            (config, mode) =>
            {
                var entry = config.EnchantPreviewModeConfig;
                if (entry != null)
                    entry.Value = mode;
            },
            mode => mode != PreviewVisibilityMode.Off,
            BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus
        );
}
