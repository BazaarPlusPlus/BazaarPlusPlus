#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewSettingsDockEntry
{
    public static CyclingSettingsDockEntry<PreviewVisibilityMode> Create(
        Action<PreviewVisibilityMode>? onChanged = null
    ) =>
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
            BppSettingsDockCatalog.ResolvePreviewVisibilityModeStatus,
            onChanged
        );
}
