#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CosmeticNames;

[BppLogEventSource]
internal static class CosmeticNamesLogEvents
{
    internal static readonly BppLogEventDefinition OverlayDegraded = new(
        BppLogFeatureScope.CosmeticNames,
        "cosmetic_names.overlay.degraded",
        [],
        new BppLogStormPolicy([])
    );
}
