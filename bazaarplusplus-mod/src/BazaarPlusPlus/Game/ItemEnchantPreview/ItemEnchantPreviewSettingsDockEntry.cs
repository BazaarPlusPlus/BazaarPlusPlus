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
            config => config.EnchantPreviewModeConfig.Value,
            (config, mode) =>
            {
                config.EnchantPreviewModeConfig.Value = mode;
            },
            mode => mode != PreviewVisibilityMode.Off,
            BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus
        );
}
