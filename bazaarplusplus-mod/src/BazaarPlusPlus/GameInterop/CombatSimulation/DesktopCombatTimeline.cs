#nullable enable
using System.Collections.Concurrent;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.GameInterop.CombatSimulation;

/// <summary>
/// The desktop host's Combat Impact adapter (ADR-0019): copies a finished game simulation and
/// the card templates' attribute modifiers into Core's combat model, in the game's iteration
/// order. Every enum crosses through <see cref="DesktopCombatFacets"/>; a value Core has no member
/// for is left out. The facet pin test (DesktopCombatFacetVocabularyTests) rules that out for the
/// configured Managed snapshot only; a newer game build can still add a member Core lacks, so the
/// first miss of each game enum logs <c>post_combat_impact.timeline.unmapped_member</c>.
/// </summary>
internal static class DesktopCombatTimeline
{
    private static readonly BppLogEvent UnmappedMemberEvent = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.timeline.unmapped_member"
    );

    // Game enums whose miss has been logged this process. The timeline is built off the main
    // thread, so the set is concurrent.
    private static readonly ConcurrentDictionary<Type, byte> ReportedUnmappedEnums = new();

    public static CombatTimeline From(CombatSim simulation)
    {
        var timeline = new CombatTimeline { Frames = new List<CombatFrame>() };
        foreach (var frame in simulation.Frames)
            timeline.Frames.Add(frame == null ? new CombatFrame() : ToFrame(frame));
        foreach (var (sourceId, stats) in simulation.CardStats)
        {
            if (sourceId == null || stats == null)
                continue;
            var copy = new Dictionary<CombatCardStat, int>();
            foreach (var (stat, value) in stats)
            {
                if (TryFacet(DesktopCombatFacets.CardStat, stat, out var facet))
                    copy[facet] = value;
            }
            timeline.CardStats[sourceId] = copy;
        }
        return timeline;
    }

    public static CombatAttributeModifier? ToModifier(TActionCardModifyAttribute? modifier) =>
        modifier == null
            ? null
            : ToModifier(modifier.AttributeType, modifier.Operation, modifier.Value);

    public static CombatAttributeModifier? ToModifier(TAuraActionCardModifyAttribute? modifier) =>
        modifier == null
            ? null
            : ToModifier(modifier.AttributeType, modifier.Operation, modifier.Value);

    public static CombatValue ToValue(ITValue? value) =>
        value switch
        {
            TFixedValue fixedValue => new CombatFixedValue { Value = fixedValue.Value },
            TRangeValue range => new CombatRangeValue
            {
                MinValue = range.MinValue,
                MaxValue = range.MaxValue,
                Modifier = ToValueModifier(range.Modifier),
            },
            TReferenceValueCardAttribute reference
                when TryFacet(
                    DesktopCombatFacets.CardAttribute,
                    reference.AttributeType,
                    out var attribute
                ) => new CombatCardAttributeReference
            {
                AttributeType = attribute,
                TargetsSelf = reference.Target is TTargetCardSelf,
                Modifier = ToValueModifier(reference.Modifier),
            },
            TReferenceValueCardAttributeUnscaled => Opaque(
                CombatOpaqueReferenceKind.CardAttributeUnscaled
            ),
            TReferenceValueAttributeChange => Opaque(CombatOpaqueReferenceKind.AttributeChange),
            TReferenceValuePlayerAttribute => Opaque(CombatOpaqueReferenceKind.PlayerAttribute),
            TReferenceValueCardCount => Opaque(CombatOpaqueReferenceKind.CardCount),
            _ => new CombatUnsupportedValue(),
        };

    private static CombatAttributeModifier? ToModifier(
        ECardAttributeType attributeType,
        EAttributeModifierOperation operation,
        ITValue? value
    ) =>
        TryFacet(DesktopCombatFacets.CardAttribute, attributeType, out var attribute)
        && TryFacet(DesktopCombatFacets.AttributeOperation, operation, out var facetOperation)
            ? new CombatAttributeModifier
            {
                AttributeType = attribute,
                Operation = facetOperation,
                Value = ToValue(value),
            }
            : null;

    private static CombatValueModifier? ToValueModifier(TValueModifier? modifier)
    {
        if (modifier == null)
            return null;
        // A mode Core lacks makes the modifier one attribution cannot evaluate.
        var known = TryFacet(
            DesktopCombatFacets.ValueModifierMode,
            modifier.ModifyMode,
            out var mode
        );
        return new CombatValueModifier
        {
            ModifyMode = mode,
            Value = known ? ToValue(modifier.Value) : new CombatUnsupportedValue(),
            ShouldRound = modifier.ShouldRound,
        };
    }

    private static CombatOpaqueReference Opaque(CombatOpaqueReferenceKind kind) =>
        new() { Kind = kind };

    private static CombatFrame ToFrame(CombatSimFrame frame)
    {
        var result = new CombatFrame
        {
            PlayerUpdates = ToPlayerUpdate(frame.PlayerUpdates),
            OpponentUpdates = ToPlayerUpdate(frame.OpponentUpdates),
        };
        if (frame.Events != null)
        {
            foreach (var candidate in frame.Events)
            {
                var converted = ToEvent(candidate);
                if (converted != null)
                    result.Events.Add(converted);
            }
        }
        if (frame.CardUpdates != null)
        {
            foreach (var (instanceId, update) in frame.CardUpdates)
            {
                if (instanceId.Value == null || update == null)
                    continue;
                result.CardUpdates[instanceId.Value] = ToCardUpdate(update);
            }
        }
        return result;
    }

    private static CombatPlayerUpdate? ToPlayerUpdate(CombatSimPlayerUpdate? update)
    {
        if (update == null)
            return null;
        var result = new CombatPlayerUpdate();
        if (update.HealthAdjustments != null)
        {
            foreach (var adjustment in update.HealthAdjustments)
            {
                if (
                    adjustment == null
                    || !TryFacet(
                        DesktopCombatFacets.DamageType,
                        adjustment.DamageType,
                        out var damageType
                    )
                    || !TryFacet(
                        DesktopCombatFacets.HealthChange,
                        adjustment.AttributeChanged,
                        out var change
                    )
                )
                    continue;
                result.HealthAdjustments.Add(
                    new CombatHealthAdjustment
                    {
                        DamageType = damageType,
                        AttributeChanged = change,
                        Amount = adjustment.Amount,
                        IsCrit = adjustment.IsCrit,
                        IsDamageReduced = adjustment.IsDamageReduced,
                    }
                );
            }
        }
        if (update.Attributes != null)
        {
            foreach (var (key, attribute) in update.Attributes)
            {
                if (
                    attribute == null
                    || !TryFacet(DesktopCombatFacets.PlayerAttribute, key, out var keyFacet)
                    || !TryFacet(
                        DesktopCombatFacets.PlayerAttribute,
                        attribute.AttributeType,
                        out var typeFacet
                    )
                )
                    continue;
                result.Attributes[keyFacet] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = typeFacet,
                    PreviousValue = attribute.PreviousValue,
                    CurrentValue = attribute.CurrentValue,
                };
            }
        }
        return result;
    }

    private static CombatCardUpdate ToCardUpdate(CombatSimCardUpdate update)
    {
        var result = new CombatCardUpdate
        {
            CardInstanceId = update.CardInstanceId.Value ?? string.Empty,
        };
        if (update.Attributes == null)
            return result;
        foreach (var (key, attribute) in update.Attributes)
        {
            if (
                attribute == null
                || !TryFacet(DesktopCombatFacets.CardAttribute, key, out var keyFacet)
                || !TryFacet(
                    DesktopCombatFacets.CardAttribute,
                    attribute.AttributeType,
                    out var typeFacet
                )
            )
                continue;
            result.Attributes[keyFacet] = new CombatCardAttributeUpdate
            {
                AttributeType = typeFacet,
                PreviousValue = attribute.PreviousValue,
                CurrentValue = attribute.CurrentValue,
            };
        }
        return result;
    }

    private static CombatEvent? ToEvent(ICombatSimEvent? candidate)
    {
        switch (candidate)
        {
            case null:
                return null;
            case CombatSimEventEffectExecuted executed:
                return TryFacet(DesktopCombatFacets.ActionType, executed.ActionType, out var action)
                    ? new CombatEffectExecuted
                    {
                        ExecutionContextId = executed.ExecutionContextId ?? string.Empty,
                        EffectId = executed.EffectId ?? string.Empty,
                        ActionType = action,
                        Source = executed.Source?.Value,
                        TriggerSource = executed.TriggerSource?.Value,
                        Target = ToTarget(executed.Target),
                    }
                    : null;
            case CombatSimEventEffectAuraExecuted aura:
                return new CombatAuraExecuted
                {
                    ExecutionContextId = aura.ExecutionContextId ?? string.Empty,
                    EffectId = aura.EffectId ?? string.Empty,
                    Source = aura.Source?.Value,
                    TriggerSource = aura.TriggerSource?.Value,
                    AppliedTo = ToTargets(aura.AppliedTo),
                    RemovedFrom = ToTargets(aura.RemovedFrom),
                };
            case CombatSimEventCombatantDied died:
                return TryFacet(DesktopCombatFacets.CombatantId, died.CombatantId, out var dead)
                    ? new CombatantDied { CombatantId = dead }
                    : null;
            case CombatSimEventCardEnchanted enchanted:
                return new CombatCardEnchanted
                {
                    InstanceId = enchanted.InstanceId ?? string.Empty,
                    EnchantmentType = DesktopCardFacets.CapturedEnchantment(
                        enchanted.EnchantmentType
                    ),
                    IsReverted = enchanted.IsReverted,
                };
            case CombatSimEventCardTransformed transformed:
                return new CombatCardTransformed
                {
                    OriginalInstanceId = transformed.OriginalInstanceId ?? string.Empty,
                    TransformedCardInstanceIds = transformed
                        .TransformedCards?.Where(card => card != null)
                        .Select(card => card.InstanceId)
                        .ToList(),
                };
            case CombatSimEventCardTransformReverted reverted:
                return new CombatCardTransformReverted
                {
                    OriginalInstanceId = reverted.OriginalCard?.InstanceId,
                    TransformedCardInstanceIds = reverted.TransformedCardInstanceIds?.ToList(),
                };
        }
        if (CardActionCostSpentEventReader.TryRead(candidate, out var costSpent))
        {
            return new CombatCardActionCostSpent
            {
                ExecutingCard = costSpent.ExecutingCard.Value ?? string.Empty,
                PlayerAttributeSpent =
                    costSpent.PlayerAttributeSpent is { } spent
                    && TryFacet(DesktopCombatFacets.PlayerAttribute, spent, out var facet)
                        ? facet
                        : null,
            };
        }
        return null;
    }

    private static HashSet<CombatTarget> ToTargets(IEnumerable<IEffectTarget>? targets)
    {
        var result = new HashSet<CombatTarget>();
        if (targets == null)
            return result;
        foreach (var target in targets)
        {
            if (ToTarget(target) is { } converted)
                result.Add(converted);
        }
        return result;
    }

    // A miss drops the value, as Core has no member for it; the first miss of each game enum is
    // logged once, naming the member, so a game update that outgrows the facets shows in the log.
    private static bool TryFacet<TSource, TFacet>(
        CardFacetNameMap<TSource, TFacet> map,
        TSource source,
        out TFacet facet
    )
        where TSource : struct, Enum
        where TFacet : struct, Enum
    {
        if (map.TryToFacet(source, out facet))
            return true;
        if (ReportedUnmappedEnums.TryAdd(typeof(TSource), 0))
        {
            BppLog.WarnEvent(
                UnmappedMemberEvent,
                ("enum", typeof(TSource).Name),
                ("member", source.ToString())
            );
        }
        return false;
    }

    public static CombatTarget? ToTarget(IEffectTarget? target) =>
        target switch
        {
            EffectTargetCard card => new CombatCardTarget
            {
                Target = card.Target.Value ?? string.Empty,
            },
            EffectTargetPlayer player
                when TryFacet(DesktopCombatFacets.CombatantId, player.Target, out var combatant) =>
                new CombatPlayerTarget { Target = combatant },
            _ => null,
        };
}
