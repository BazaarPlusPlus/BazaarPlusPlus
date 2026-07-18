#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal enum OverlayTickFailureReasonCode
{
    CallbackException,
}

internal enum OverlayDirectiveFailureReasonCode
{
    CallbackException,
}

internal enum OverlayCombatProbeFailureReasonCode
{
    ReadFailed,
}

[BppLogEventSource]
internal static class OverlayPanelLogEvents
{
    internal static readonly BppLogFieldDefinition TickDegradedPanelId = Public(
        0,
        "panel_id",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition TickDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition TickDegraded = new(
        BppLogFeatureScope.OverlayPanels,
        "overlay_panels.host.tick_degraded",
        [TickDegradedPanelId, TickDegradedReasonCode],
        new BppLogStormPolicy([TickDegradedPanelId, TickDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition TickRecoveredPanelId = Public(
        0,
        "panel_id",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition TickRecovered = new(
        BppLogFeatureScope.OverlayPanels,
        "overlay_panels.host.tick_recovered",
        [TickRecoveredPanelId]
    );

    internal static readonly BppLogFieldDefinition DirectiveFailedRequestId = Public(
        0,
        "request_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition DirectiveFailedPanelId = Public(
        1,
        "panel_id",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DirectiveFailedDirective = Public(
        2,
        "directive",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DirectiveFailedReasonCode = Public(
        3,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition DirectiveFailed = new(
        BppLogFeatureScope.OverlayPanels,
        "overlay_panels.directive.failed",
        [
            DirectiveFailedRequestId,
            DirectiveFailedPanelId,
            DirectiveFailedDirective,
            DirectiveFailedReasonCode,
        ]
    );

    internal static readonly BppLogFieldDefinition CombatProbeDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CombatProbeDegraded = new(
        BppLogFeatureScope.OverlayPanels,
        "overlay_panels.combat_probe.degraded",
        [CombatProbeDegradedReasonCode],
        new BppLogStormPolicy([CombatProbeDegradedReasonCode])
    );

    internal static readonly BppLogEventDefinition CombatProbeRecovered = new(
        BppLogFeatureScope.OverlayPanels,
        "overlay_panels.combat_probe.recovered",
        []
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);
}
