#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EndOfRunScreenshot;

    public BppSettingsDockDefinition Build(BppConfig config) =>
        BppSettingsDockDefinition.Toggle(
            "EndOfRunScreenshot",
            EndOfRunScreenshotSettingsMenuLabel.Resolve,
            () => EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(config),
            enabled => WriteEnabled(config, enabled),
            isInteractable: () => !EndOfRunScreenshotSettingsPolicy.IsForcedOn(config)
        );

    private static void WriteEnabled(BppConfig config, bool enabled)
    {
        config.EndOfRunScreenshotEnabledConfig.Value = enabled;
    }
}
