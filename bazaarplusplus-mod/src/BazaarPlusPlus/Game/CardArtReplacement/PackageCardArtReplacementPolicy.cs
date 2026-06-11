#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal static class PackageCardArtReplacementPolicy
{
    internal const bool DefaultEnabled = false;

    internal static bool IsEnabled(IBppConfig? config) =>
        config?.EnablePackageCardArtReplacementConfig?.Value ?? DefaultEnabled;

    internal static void SetEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EnablePackageCardArtReplacementConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
