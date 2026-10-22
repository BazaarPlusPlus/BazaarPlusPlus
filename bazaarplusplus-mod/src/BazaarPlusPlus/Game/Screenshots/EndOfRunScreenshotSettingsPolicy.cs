#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunScreenshotSettingsPolicy
{
    internal static bool IsEnabledOrForced(BppSettings config)
    {
        return ReadEnabled(config) || IsForcedOn(config);
    }

    internal static bool IsForcedOn(BppSettings config)
    {
        return config.BazaarDbUploadEnabled.Value || config.UseFixedSupporterListConfig.Value;
    }

    internal static void ForceEnabled(BppSettings config)
    {
        config.EndOfRunScreenshotEnabledConfig.Value = true;
    }

    private static bool ReadEnabled(BppSettings config)
    {
        return config.EndOfRunScreenshotEnabledConfig.Value;
    }
}
