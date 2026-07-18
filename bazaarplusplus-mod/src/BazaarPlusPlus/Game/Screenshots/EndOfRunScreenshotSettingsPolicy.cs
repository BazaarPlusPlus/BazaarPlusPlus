#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunScreenshotSettingsPolicy
{
    internal static bool IsEnabledOrForced(IBppConfig config)
    {
        return ReadEnabled(config) || IsForcedOn(config);
    }

    internal static bool IsForcedOn(IBppConfig config)
    {
        return (config.BazaarDbUploadEnabled?.Value ?? false)
            || (config.UseFixedSupporterListConfig?.Value ?? false);
    }

    internal static void ForceEnabled(IBppConfig config)
    {
        var entry = config.EndOfRunScreenshotEnabledConfig;
        if (entry != null)
            entry.Value = true;
    }

    private static bool ReadEnabled(IBppConfig config)
    {
        return config.EndOfRunScreenshotEnabledConfig?.Value ?? true;
    }
}
