#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

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

    private static bool ReadEnabled(BppConfig config) => config.BazaarDbUploadEnabled.Value;

    private static void WriteEnabled(BppConfig config, bool enabled)
    {
        config.BazaarDbUploadEnabled.Value = enabled;

        if (enabled)
            EndOfRunScreenshotSettingsPolicy.ForceEnabled(config);
    }
}

internal static class BazaarDbBundleSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "BazaarDB upload",
        "BazaarDB 数据共建",
        "BazaarDB-Upload",
        "Subir a BazaarDB",
        "BazaarDB 업로드",
        "Carica su BazaarDB"
    );

    internal static string Resolve(string languageCode) =>
        Labels.Resolve(languageCode, L.CurrentMode);
}
