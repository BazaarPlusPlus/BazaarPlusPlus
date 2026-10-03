#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunScreenshotSettingsPolicy
{
    internal static bool IsEnabledOrForced(BppConfig config)
    {
        return ReadEnabled(config) || IsForcedOn(config);
    }

    internal static bool IsForcedOn(BppConfig config)
    {
        return config.BazaarDbUploadEnabled.Value || config.UseFixedSupporterListConfig.Value;
    }

    internal static void ForceEnabled(BppConfig config)
    {
        config.EndOfRunScreenshotEnabledConfig.Value = true;
    }

    private static bool ReadEnabled(BppConfig config)
    {
        return config.EndOfRunScreenshotEnabledConfig.Value;
    }
}
