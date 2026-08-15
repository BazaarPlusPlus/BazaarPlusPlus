#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal static class CombatImpactTargetDetailPolicy
{
    internal static bool ShouldRender(
        CombatImpactKind kind,
        string nativeAttributeKey,
        CombatImpactEventSurface surface
    ) =>
        kind
            is not (
                CombatImpactKind.DirectDamage
                or CombatImpactKind.Burn
                or CombatImpactKind.Poison
                or CombatImpactKind.Healing
                or CombatImpactKind.Shield
            )
        && (
            kind != CombatImpactKind.AttributeChange
            || surface == CombatImpactEventSurface.CardAttribute
            || nativeAttributeKey
                is not (
                    "RegenApplyAmount"
                    or "BurnRemoveAmount"
                    or "PoisonRemoveAmount"
                    or "RegenRemoveAmount"
                    or "ShieldRemoveAmount"
                    or "RageRemoveAmount"
                    or "HealthMax"
                    or "HealthRegen"
                    or "Rage"
                    or "Tempo"
                    or "TempoApplyAmount"
                    or "TempoRemoveAmount"
                )
        );
}
