#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Supporters;

internal static class FixedSupporterListSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.FixedSupporterList,
            "StreamMode",
            FixedSupporterListSettingsMenuLabel.Resolve,
            ReadEnabled,
            WriteEnabled
        );

    private static bool ReadEnabled(BppSettings config) => config.UseFixedSupporterListConfig.Value;

    private static void WriteEnabled(BppSettings config, bool enabled)
    {
        config.UseFixedSupporterListConfig.Value = enabled;

        if (enabled)
            EndOfRunScreenshotSettingsPolicy.ForceEnabled(config);
    }
}
