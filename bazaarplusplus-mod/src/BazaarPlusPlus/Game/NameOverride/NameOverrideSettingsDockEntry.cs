#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.NameOverride;

internal sealed class NameOverrideSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.NameOverride;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "NameOverride",
            NameOverrideSettingsMenuLabel.Resolve,
            new NameOverrideSettingsMenuBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled),
                NameOverrideUiRefresh.TryRefreshVisibleHeroBanners
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.EnableNameOverrideConfig?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EnableNameOverrideConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
