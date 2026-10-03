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

    private static bool ReadEnabled(BppConfig config) => config.EnableNameOverrideConfig.Value;

    private static void WriteEnabled(BppConfig config, bool enabled)
    {
        config.EnableNameOverrideConfig.Value = enabled;
    }
}
