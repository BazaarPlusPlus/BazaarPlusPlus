#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EndOfRunScreenshot;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        BppSettingsDockDefinition.Toggle(
            "EndOfRunScreenshot",
            EndOfRunScreenshotSettingsMenuLabel.Resolve,
            () => EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(config),
            enabled => WriteEnabled(config, enabled),
            isInteractable: () => !EndOfRunScreenshotSettingsPolicy.IsForcedOn(config)
        );

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EndOfRunScreenshotEnabledConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
