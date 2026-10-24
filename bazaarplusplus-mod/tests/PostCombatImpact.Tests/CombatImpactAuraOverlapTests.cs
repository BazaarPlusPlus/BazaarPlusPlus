using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed partial class CombatImpactProjectorTests
{
    [Fact]
    public void Latest_replay_crit_shield_gain_excludes_same_frame_tempo_scaling()
    {
        // Battle 58ba48dad8b24f46883a050c234991b7 (2026-09-07). Exact shield transitions
        // from the twelve Improvised Protection executions, not twelve synthetic +10 frames.
        var transitions = new (int Before, int After)[]
        {
            (92, 192),
            (552, 682),
            (682, 782),
            (782, 882),
            (882, 982),
            (982, 1082),
            (1112, 1212),
            (1332, 1432),
            (1432, 1652),
            (1652, 1752),
            (1782, 1882),
            (1882, 1982),
        };
        var (simulation, entities) = AuraOverlap(CombatCardAttribute.ShieldApplyAmount, 10, 100);
        simulation.Frames.Clear();
        foreach (var (before, after) in transitions)
        {
            var (single, _) = AuraOverlap(
                CombatCardAttribute.ShieldApplyAmount,
                10,
                after - before
            );
            var frame = single.Frames[0];
            frame
                .CardUpdates[("target")]
                .Attributes[CombatCardAttribute.ShieldApplyAmount]
                .PreviousValue = before;
            frame
                .CardUpdates[("target")]
                .Attributes[CombatCardAttribute.ShieldApplyAmount]
                .CurrentValue = after;
            simulation.Frames.Add(frame);
        }
        var report = CombatImpactProjector.Project(simulation, entities);
        var gain = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(12, gain.Count);
        Assert.Equal(120, gain.ObservedValue);
        Assert.False(gain.HasUnattributedTransitionValue);
        Assert.Equal(1230, report.AttributeTransitionDiagnostics.Sum(item => item.ResidualValue));
        Assert.All(
            report.AttributeTransitionDiagnostics,
            item => Assert.Equal(item.NetValue, item.AttributedValue + item.ResidualValue)
        );
    }

    [Fact]
    public void All_card_attribute_gains_exclude_overlapping_aura_recalculations()
    {
        foreach (
            var attribute in new[]
            {
                CombatCardAttribute.ShieldApplyAmount,
                CombatCardAttribute.DamageAmount,
                CombatCardAttribute.HealAmount,
                CombatCardAttribute.BurnApplyAmount,
                CombatCardAttribute.PoisonApplyAmount,
                CombatCardAttribute.CritChance,
                CombatCardAttribute.CooldownMax,
                CombatCardAttribute.SellPrice,
            }
        )
        {
            var (simulation, entities) = AuraOverlap(attribute, 10, 130);
            var report = CombatImpactProjector.Project(simulation, entities);
            var gain = Assert.Single(Assert.Single(report.Sources).Groups);
            Assert.Equal(attribute.ToString(), gain.NativeAttributeKey);
            Assert.Equal(10, gain.ObservedValue);
            Assert.Equal(120, Assert.Single(report.AttributeTransitionDiagnostics).ResidualValue);
        }
    }

    [Fact]
    public void Opposing_aura_changes_preserve_signed_actions_even_with_zero_net_change()
    {
        foreach (var (amount, net) in new[] { (10, 0), (10, -20), (-10, 20) })
        {
            var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, amount, net);
            var report = CombatImpactProjector.Project(simulation, entities);
            Assert.Equal(amount, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
            var diagnostic = Assert.Single(report.AttributeTransitionDiagnostics);
            Assert.Equal(net, diagnostic.AttributedValue + diagnostic.ResidualValue);
        }
    }

    [Fact]
    public void Unknown_action_is_not_solved_from_a_net_delta_containing_aura_changes()
    {
        var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, 10, 130);
        entities["source"] = entities["source"] with
        {
            AbilityAttributeModifiersByEffectId = new Dictionary<string, CombatAttributeModifier>
            {
                ["gain"] = new()
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    Operation = CombatAttributeOperation.Add,
                    Value = new CombatRangeValue { MinValue = 1, MaxValue = 200 },
                },
            },
        };
        simulation.Frames[0].Events.Add(AttributeExecution("trigger", "gain", "target"));
        entities["trigger"] = entities["source"] with
        {
            Id = "trigger",
            AbilityAttributeModifiersByEffectId = new Dictionary<string, CombatAttributeModifier>
            {
                ["gain"] = new()
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    Operation = CombatAttributeOperation.Add,
                    Value = new CombatFixedValue { Value = 10 },
                },
            },
        };
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Null(
            Assert
                .Single(report.Sources.Single(source => source.Entity.Id == "source").Groups)
                .ObservedValue
        );
        Assert.Equal(
            10,
            Assert
                .Single(report.Sources.Single(source => source.Entity.Id == "trigger").Groups)
                .ObservedValue
        );
        var diagnostic = Assert.Single(report.AttributeTransitionDiagnostics);
        Assert.Equal(120, diagnostic.ResidualValue);
    }

    [Fact]
    public void Multiplicative_or_unidentified_aura_overlap_does_not_invent_an_amount()
    {
        foreach (var unknown in new[] { false, true })
        {
            var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, 10, 130);
            entities["target"] = entities["target"] with
            {
                AuraAttributeModifiersByEffectId = unknown
                    ? null
                    : new Dictionary<string, CombatAttributeModifier>
                    {
                        ["formula"] = new()
                        {
                            AttributeType = CombatCardAttribute.DamageAmount,
                            Operation = CombatAttributeOperation.Multiply,
                            Value = new CombatFixedValue { Value = 2 },
                        },
                    },
            };
            var report = CombatImpactProjector.Project(simulation, entities);
            Assert.Null(Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
            Assert.Equal(130, Assert.Single(report.AttributeTransitionDiagnostics).ResidualValue);
        }
    }

    [Fact]
    public void Same_frame_source_reference_changes_are_not_read_as_execution_local_values()
    {
        var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, 10, 130);
        var modifier = entities["source"].AbilityAttributeModifiersByEffectId!["gain"] with
        {
            Value = new CombatCardAttributeReference
            {
                AttributeType = CombatCardAttribute.Custom_0,
                TargetsSelf = true,
            },
        };
        entities["source"] = entities["source"] with
        {
            AbilityAttributeModifiersByEffectId = new Dictionary<string, CombatAttributeModifier>
            {
                ["gain"] = modifier,
            },
        };
        SetSourceAttributeUpdate(simulation.Frames[0], CombatCardAttribute.Custom_0, 10, 20);
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Null(Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
    }

    [Fact]
    public void Hidden_self_formula_still_blocks_another_aura_from_claiming_its_delta()
    {
        var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, 10, 130);
        simulation.Frames[0].Events.RemoveAt(0);
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    Source = ("source"),
                    EffectId = "other",
                    AppliedTo = { CardTarget("target") },
                }
            );
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Empty(report.Sources);
    }

    [Fact]
    public void Aura_removal_is_also_a_competing_transition()
    {
        var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, 10, -20);
        var aura = Assert.Single(simulation.Frames[0].Events.OfType<CombatAuraExecuted>());
        aura.AppliedTo.Clear();
        aura.RemovedFrom.Add(CardTarget("target"));
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Equal(10, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
        Assert.Equal(-30, Assert.Single(report.AttributeTransitionDiagnostics).ResidualValue);
    }

    [Fact]
    public void Player_modifier_and_aura_cannot_both_claim_the_same_net_gain()
    {
        var simulation = new CombatTimeline();
        var frame = simulation.Frames[0];
        frame.Events.Add(
            Executed("source", CombatActionType.PlayerModifyAttribute, Player(Combatant.Player))
        );
        frame.Events.Add(
            new CombatAuraExecuted
            {
                Source = ("trigger"),
                AppliedTo = { Player(Combatant.Player) },
            }
        );
        frame.PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.HealthMax] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.HealthMax,
                    PreviousValue = 100,
                    CurrentValue = 130,
                },
            },
        };
        var report = CombatImpactProjector.Project(simulation, Entities());
        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
        var diagnostic = Assert.Single(report.AttributeTransitionDiagnostics);
        Assert.Equal(30, diagnostic.ResidualValue);
        Assert.Equal(0, diagnostic.AttributedValue);
    }

    [Fact]
    public void Aura_on_an_unrelated_attribute_does_not_hide_the_explicit_gain()
    {
        var (simulation, entities) = AuraOverlap(CombatCardAttribute.DamageAmount, 10, 10);
        entities["target"] = entities["target"] with
        {
            AuraAttributeModifiersByEffectId = new Dictionary<string, CombatAttributeModifier>
            {
                ["formula"] = new()
                {
                    AttributeType = CombatCardAttribute.ShieldApplyAmount,
                    Operation = CombatAttributeOperation.Add,
                    Value = new CombatFixedValue { Value = 100 },
                },
            },
        };
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Equal(10, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
    }

    private static (CombatTimeline, Dictionary<string, CombatImpactEntity>) AuraOverlap(
        CombatCardAttribute attribute,
        int amount,
        int net
    )
    {
        var simulation = new CombatTimeline();
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = AttributeModifierEntity(
            "source",
            "Skill",
            "gain",
            new CombatAttributeModifier
            {
                AttributeType = attribute,
                Operation = CombatAttributeOperation.Add,
                Value = new CombatFixedValue { Value = amount },
            },
            0
        );
        entities["target"] = entities["target"] with
        {
            ReferenceValuedAuraEffectIds = ["formula"],
            AuraAttributeTypesByEffectId = new Dictionary<string, CombatCardAttribute>
            {
                ["formula"] = attribute,
            },
            AuraAttributeModifiersByEffectId = new Dictionary<string, CombatAttributeModifier>
            {
                ["formula"] = new()
                {
                    AttributeType = attribute,
                    Operation = CombatAttributeOperation.Add,
                    Value = new CombatOpaqueReference
                    {
                        Kind = CombatOpaqueReferenceKind.PlayerAttribute,
                    },
                },
            },
        };
        var frame = simulation.Frames[0];
        frame.Events.Add(AttributeExecution("source", "gain", "target"));
        frame.Events.Add(
            new CombatAuraExecuted
            {
                Source = ("target"),
                EffectId = "formula",
                AppliedTo = { CardTarget("target") },
            }
        );
        frame.CardUpdates[("target")] = new CombatCardUpdate
        {
            CardInstanceId = ("target"),
            Attributes =
            {
                [attribute] = new CombatCardAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = 100,
                    CurrentValue = 100 + net,
                },
            },
        };
        return (simulation, entities);
    }
}
