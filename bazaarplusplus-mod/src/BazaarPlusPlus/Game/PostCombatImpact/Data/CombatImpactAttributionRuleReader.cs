#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactAttributionRuleReader
{
    internal static IReadOnlyDictionary<
        string,
        CombatImpactPrerequisiteSkillSourceRule
    >? ReadSourceRules(IEnumerable<TCardAbility>? abilities, IEnumerable<TCardAura>? auras)
    {
        var rules = new Dictionary<string, CombatImpactPrerequisiteSkillSourceRule>(
            StringComparer.Ordinal
        );
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        if (abilities != null)
        {
            foreach (var ability in abilities)
                AddSourceRule(ability?.Id, ability?.Prerequisites, rules, ambiguous);
        }
        if (auras != null)
        {
            foreach (var aura in auras)
                AddSourceRule(aura?.Id, aura?.Prerequisites, rules, ambiguous);
        }
        return rules.Count == 0 ? null : rules;
    }

    internal static IReadOnlyCollection<CombatImpactUseAttributionRule>? ReadUseRules(
        IEnumerable<TCardAbility>? abilities
    )
    {
        if (abilities == null)
            return null;

        var rules = new List<CombatImpactUseAttributionRule>();
        foreach (var ability in abilities)
        {
            if (TryReadUseRule(ability, out var rule))
                rules.Add(rule);
        }
        return rules.Count == 0 ? null : rules;
    }

    internal static bool TryReadUseRule(
        TCardAbility? ability,
        out CombatImpactUseAttributionRule rule
    )
    {
        rule = null!;
        if (
            ability == null
            || string.IsNullOrWhiteSpace(ability.Id)
            || ability.ActiveIn != EEffectActiveIn.HandOnly
            || ability.WorksIn != EEffectWorksIn.Anywhere
            || ability.Trigger is not TTriggerOnItemUsed { Subject: TTargetCardOccupying occupying }
            || !TryReadItemCondition(occupying.Conditions, out var itemCondition)
            || ability.Action
                is not TActionPlayerModifyAttribute
                {
                    AttributeType: EPlayerAttributeType.Tempo,
                    Operation: EAttributeModifierOperation.Add,
                    Value: TFixedValue fixedValue,
                    Duration: null,
                    Cost: null,
                    Target: TTargetPlayerRelative
                    {
                        TargetMode: ETargetPlayerRelativeTargetMode.Self,
                        Conditions: null,
                    },
                }
            || !TryPositiveInt(fixedValue.Value, out var amount)
            || !TryReadSourceRule(ability.Id, ability.Prerequisites, out var sourceRule)
            || sourceRule.SkillTiers.Count == 0
        )
            return false;

        rule = new CombatImpactUseAttributionRule(sourceRule, itemCondition, amount);
        return true;
    }

    private static void AddSourceRule(
        string? effectId,
        IReadOnlyList<ITPrerequisite>? prerequisites,
        IDictionary<string, CombatImpactPrerequisiteSkillSourceRule> rules,
        ISet<string> ambiguous
    )
    {
        if (
            !TryReadSourceRule(effectId, prerequisites, out var rule)
            || ambiguous.Contains(rule.EffectId)
        )
            return;
        if (!rules.TryGetValue(rule.EffectId, out var existing))
        {
            rules[rule.EffectId] = rule;
            return;
        }
        if (
            existing.SkillTemplateId == rule.SkillTemplateId
            && existing.SkillTiers.Count == rule.SkillTiers.Count
            && existing.SkillTiers.All(rule.SkillTiers.Contains)
        )
            return;

        rules.Remove(rule.EffectId);
        ambiguous.Add(rule.EffectId);
    }

    private static bool TryReadSourceRule(
        string? effectId,
        IReadOnlyList<ITPrerequisite>? prerequisites,
        out CombatImpactPrerequisiteSkillSourceRule rule
    )
    {
        rule = null!;
        if (
            string.IsNullOrWhiteSpace(effectId)
            || prerequisites is not [TPrerequisiteCardCount prerequisite]
            || prerequisite.Comparison != EComparisonOperator.GreaterThanOrEqual
            || prerequisite.Amount != 1
            || prerequisite.Subject
                is not TTargetCardSection
                {
                    TargetSection: ETargetCardSectionTargetSection.AbsolutePlayerSkills
                        or ETargetCardSectionTargetSection.SelfSkills,
                    ExcludeSelf: false,
                    Conditions: { } condition,
                }
            || !TryReadSkillIdentity(condition, out var templateId, out var tiers)
        )
            return false;

        rule = new CombatImpactPrerequisiteSkillSourceRule(effectId!, templateId, tiers);
        return true;
    }

    private static bool TryReadSkillIdentity(
        ITCardConditional condition,
        out Guid templateId,
        out IReadOnlyCollection<ETier> tiers
    )
    {
        templateId = Guid.Empty;
        tiers = [];
        IReadOnlyList<ITCardConditional> conditions = condition is TCardConditionalAnd and
            ? and.Conditions
            : [condition];
        if (conditions.Count is < 1 or > 2)
            return false;

        TCardConditionalId? id = null;
        TCardConditionalTier? tier = null;
        foreach (var candidate in conditions)
        {
            switch (candidate)
            {
                case TCardConditionalId cardId when id == null:
                    id = cardId;
                    break;
                case TCardConditionalTier cardTier when tier == null:
                    tier = cardTier;
                    break;
                default:
                    return false;
            }
        }

        if (id is not { IsNot: false } || id.Id == Guid.Empty)
            return false;
        if (tier is { IsNot: true } || tier is { Tiers.Count: 0 })
            return false;

        templateId = id.Id;
        tiers = tier?.Tiers.ToArray() ?? [];
        return true;
    }

    private static bool TryReadItemCondition(
        ITCardConditional? condition,
        out CombatImpactItemTagCondition itemCondition
    )
    {
        switch (condition)
        {
            case TCardConditionalTag { Tags.Count: > 0 } tags:
                itemCondition = new CombatImpactItemTagCondition(
                    tags.Operator,
                    PublicTags: tags.Tags.ToArray()
                );
                return true;
            case TCardConditionalHiddenTag { Tags.Count: > 0 } hiddenTags:
                itemCondition = new CombatImpactItemTagCondition(
                    hiddenTags.Operator,
                    HiddenTags: hiddenTags.Tags.ToArray()
                );
                return true;
            default:
                itemCondition = null!;
                return false;
        }
    }

    private static bool TryPositiveInt(float value, out int amount)
    {
        if (
            !float.IsFinite(value)
            || value <= 0
            || value > int.MaxValue
            || MathF.Truncate(value) != value
        )
        {
            amount = 0;
            return false;
        }

        amount = (int)value;
        return true;
    }
}
