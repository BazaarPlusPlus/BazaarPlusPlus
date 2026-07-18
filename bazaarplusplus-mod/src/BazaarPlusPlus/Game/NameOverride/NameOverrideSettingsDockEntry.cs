#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create(Action? refreshUi = null) =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.NameOverride,
            "NameOverride",
            NameOverrideSettingsMenuLabel.Resolve,
            ReadEnabled,
            WriteEnabled,
            _ =>
            {
                if (refreshUi != null)
                    refreshUi();
                else
                    NameOverrideUiRefresh.TryRefreshVisibleHeroBanners();
            }
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
