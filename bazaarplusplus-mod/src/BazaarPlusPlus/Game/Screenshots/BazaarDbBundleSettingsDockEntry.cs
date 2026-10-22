#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class BazaarDbBundleSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.BazaarDbUpload,
            "BazaarDbUpload",
            BazaarDbBundleSettingsMenuLabel.Resolve,
            ReadEnabled,
            WriteEnabled
        );

    private static bool ReadEnabled(BppSettings config) => config.BazaarDbUploadEnabled.Value;

    private static void WriteEnabled(BppSettings config, bool enabled)
    {
        config.BazaarDbUploadEnabled.Value = enabled;

        if (enabled)
            EndOfRunScreenshotSettingsPolicy.ForceEnabled(config);
    }
}
