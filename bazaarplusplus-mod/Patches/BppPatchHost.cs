#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Patches;

internal static class BppPatchHost
{
    private static IBppServices? _services;

    public static IBppServices Services =>
        _services
        ?? throw new InvalidOperationException(
            "BppPatchHost.Install must be called before patches run."
        );

    public static void Install(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public static void Reset()
    {
        _services = null;
    }
}
