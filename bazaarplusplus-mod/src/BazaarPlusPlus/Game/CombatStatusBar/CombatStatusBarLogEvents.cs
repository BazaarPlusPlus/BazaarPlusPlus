#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal enum CombatSpeedLogCategory
{
    Half,
    TwoThirds,
    Normal,
    Custom,
}

[BppLogEventSource]
internal static class CombatStatusBarLogEvents
{
    internal static readonly BppLogEventDefinition NativeSkinReady = new(
        BppLogFeatureScope.CombatStatusBar,
        "combat_status_bar.native_skin.ready",
        []
    );
    internal static readonly BppLogEventDefinition NativeSkinUnavailable = new(
        BppLogFeatureScope.CombatStatusBar,
        "combat_status_bar.native_skin.unavailable",
        []
    );
    internal static readonly BppLogFieldDefinition ConfigLoadedSpeedMultiplier = Public(
        1,
        "speed_multiplier",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ConfigLoaded = new(
        BppLogFeatureScope.CombatStatusBar,
        "combat_status_bar.config.loaded",
        [ConfigLoadedSpeedMultiplier]
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogCorrelationPolicy.None, cardinality);
}
