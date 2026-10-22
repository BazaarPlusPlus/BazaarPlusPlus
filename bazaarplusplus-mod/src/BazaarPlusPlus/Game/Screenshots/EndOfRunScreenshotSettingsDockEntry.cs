#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EndOfRunScreenshot;

    public BppSettingsDockDefinition Build(BppSettings config) =>
        BppSettingsDockDefinition.Toggle(
            "EndOfRunScreenshot",
            EndOfRunScreenshotSettingsMenuLabel.Resolve,
            () => EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(config),
            enabled => WriteEnabled(config, enabled),
            isInteractable: () => !EndOfRunScreenshotSettingsPolicy.IsForcedOn(config)
        );

    private static void WriteEnabled(BppSettings config, bool enabled)
    {
        config.EndOfRunScreenshotEnabledConfig.Value = enabled;
    }
}
