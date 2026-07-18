#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Patches;

internal static class BppPatchHost
{
    private static IBppServices? _services;
    private static BppPatchFeatures? _features;

    public static IBppServices Services =>
        _services
        ?? throw new InvalidOperationException(
            "BppPatchHost.Install must be called before patches run."
        );

    internal static BppPatchFeatures Features =>
        _features
        ?? throw new InvalidOperationException(
            "BppPatchHost.Install must be called before feature patches run."
        );

    internal static bool TryGetFeatures(out BppPatchFeatures? features)
    {
        features = _features;
        return features != null;
    }

    public static void Install(IBppServices services, BppPatchFeatures features)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _features = features ?? throw new ArgumentNullException(nameof(features));
    }

    public static void Reset()
    {
        _services = null;
        _features = null;
    }
}
