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

    private static bool ReadEnabled(IBppConfig config) =>
        config.UseFixedSupporterListConfig?.Value
        ?? BPPSupporterListSourcePolicy.DefaultUseFixedList;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.UseFixedSupporterListConfig;
        if (entry != null)
            entry.Value = enabled;

        if (enabled)
            EndOfRunScreenshotSettingsPolicy.ForceEnabled(config);
    }
}
