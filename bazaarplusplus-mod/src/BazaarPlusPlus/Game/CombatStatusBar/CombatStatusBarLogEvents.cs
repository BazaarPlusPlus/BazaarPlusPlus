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
    internal static readonly BppLogFieldDefinition ConfigLoadedEnabled = Public(
        0,
        "enabled",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ConfigLoadedSpeedMultiplier = Public(
        1,
        "speed_multiplier",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ConfigLoaded = new(
        BppLogFeatureScope.CombatStatusBar,
        "combat_status_bar.config.loaded",
        [ConfigLoadedEnabled, ConfigLoadedSpeedMultiplier]
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);
}
