using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactAttributionRuleReaderTests
{
    [Fact]
    public void Reads_on_card_critted_ability_ids_and_priorities()
    {
        var abilities = new[]
        {
            new TCardAbility
            {
                Id = "crit-low",
                Trigger = new BazaarGameShared.Domain.Effect.Trigger.TTriggerOnCardCritted(),
                Priority = EEffectPriority.Low,
            },
            new TCardAbility
            {
                Id = "crit-immediate",
                Trigger = new BazaarGameShared.Domain.Effect.Trigger.TTriggerOnCardCritted(),
                Priority = EEffectPriority.Immediate,
            },
            new TCardAbility
            {
                Id = "fired",
                Trigger = new BazaarGameShared.Domain.Effect.Trigger.TTriggerOnCardFired(),
            },
        };

        var triggers = CombatImpactCriticalTriggerReader.Read(abilities);

        Assert.Equal(2, triggers?.Count);
        Assert.Equal(EEffectPriority.Low, triggers?["crit-low"]);
        Assert.Equal(EEffectPriority.Immediate, triggers?["crit-immediate"]);
        Assert.False(triggers?.ContainsKey("fired"));
    }

    [Fact]
    public void Reads_minor_on_use_tempo_rule_from_public_item_tag_graph()
    {
        var skillId = Guid.NewGuid();
        var ability = MinorAbility(
            skillId,
            new TCardConditionalTag
            {
                Operator = EListComparisonOperator.Any,
                Tags = [ECardTag.Weapon],
            },
            amount: 2
        );

        Assert.True(CombatImpactAttributionRuleReader.TryReadUseRule(ability, out var rule));

        Assert.Equal("minor-tempo", rule.SourceRule.EffectId);
        Assert.Equal(skillId, rule.SourceRule.SkillTemplateId);
        Assert.Equal([ETier.Gold], rule.SourceRule.SkillTiers);
        Assert.Equal(2, rule.FixedTempoAmount);
        Assert.Equal([ECardTag.Weapon], rule.ItemCondition.PublicTags);
    }

    [Fact]
    public void Reads_minor_on_use_tempo_rule_from_hidden_item_tag_graph()
    {
        var ability = MinorAbility(
            Guid.NewGuid(),
            new TCardConditionalHiddenTag
            {
                Operator = EListComparisonOperator.Any,
                Tags = [EHiddenTag.Haste],
            },
            amount: 3
        );

        Assert.True(CombatImpactAttributionRuleReader.TryReadUseRule(ability, out var rule));

        Assert.Equal([EHiddenTag.Haste], rule.ItemCondition.HiddenTags);
        Assert.Equal(3, rule.FixedTempoAmount);
    }

    [Fact]
    public void Reads_source_mapping_for_prerequisite_gated_aura_without_tier_condition()
    {
        var skillId = Guid.NewGuid();
        var aura = new TCardAura
        {
            Id = "major-multicast",
            Prerequisites = [SkillPrerequisite(skillId, includeTier: false)],
        };

        var rules = CombatImpactAttributionRuleReader.ReadSourceRules(null, [aura]);
        var rule = Assert.Single(rules!).Value;

        Assert.Equal(skillId, rule.SkillTemplateId);
        Assert.Empty(rule.SkillTiers);
    }

    [Fact]
    public void Rejects_similar_abilities_that_are_not_fixed_additive_tempo_use_rules()
    {
        var skillId = Guid.NewGuid();
        var wrongOperation = MinorAbility(
            skillId,
            new TCardConditionalTag
            {
                Operator = EListComparisonOperator.Any,
                Tags = [ECardTag.Weapon],
            },
            amount: 2
        );
        wrongOperation = wrongOperation with
        {
            Action = ((TActionPlayerModifyAttribute)wrongOperation.Action) with
            {
                Operation = EAttributeModifierOperation.Multiply,
            },
        };
        var fractional = MinorAbility(
            skillId,
            new TCardConditionalTag
            {
                Operator = EListComparisonOperator.Any,
                Tags = [ECardTag.Weapon],
            },
            amount: 1.5f
        );
        var extraPrerequisite = MinorAbility(
            skillId,
            new TCardConditionalTag
            {
                Operator = EListComparisonOperator.Any,
                Tags = [ECardTag.Weapon],
            },
            amount: 2
        );
        extraPrerequisite.Prerequisites!.Add(SkillPrerequisite(Guid.NewGuid(), includeTier: true));

        Assert.False(CombatImpactAttributionRuleReader.TryReadUseRule(wrongOperation, out _));
        Assert.False(CombatImpactAttributionRuleReader.TryReadUseRule(fractional, out _));
        Assert.False(CombatImpactAttributionRuleReader.TryReadUseRule(extraPrerequisite, out _));
    }

    private static TCardAbility MinorAbility(
        Guid skillId,
        ITCardConditional itemCondition,
        float amount
    ) =>
        new()
        {
            Id = "minor-tempo",
            ActiveIn = EEffectActiveIn.HandOnly,
            WorksIn = EEffectWorksIn.Anywhere,
            Trigger = new TTriggerOnItemUsed
            {
                Subject = new TTargetCardOccupying { Conditions = itemCondition },
            },
            Action = new TActionPlayerModifyAttribute
            {
                AttributeType = EPlayerAttributeType.Tempo,
                Operation = EAttributeModifierOperation.Add,
                Value = new TFixedValue { Value = amount },
                Target = new TTargetPlayerRelative
                {
                    TargetMode = ETargetPlayerRelativeTargetMode.Self,
                },
            },
            Prerequisites = [SkillPrerequisite(skillId, includeTier: true)],
        };

    private static TPrerequisiteCardCount SkillPrerequisite(Guid skillId, bool includeTier)
    {
        var conditions = new List<ITCardConditional> { new TCardConditionalId { Id = skillId } };
        if (includeTier)
            conditions.Add(new TCardConditionalTier { Tiers = [ETier.Gold] });

        return new TPrerequisiteCardCount
        {
            Comparison = EComparisonOperator.GreaterThanOrEqual,
            Amount = 1,
            Subject = new TTargetCardSection
            {
                TargetSection = ETargetCardSectionTargetSection.AbsolutePlayerSkills,
                Conditions = new TCardConditionalAnd { Conditions = conditions },
            },
        };
    }
}
