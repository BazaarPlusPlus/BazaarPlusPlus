using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
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
        var (simulation, entities) = AuraOverlap(ECardAttributeType.ShieldApplyAmount, 10, 100);
        simulation.Frames.Clear();
        foreach (var (before, after) in transitions)
        {
            var (single, _) = AuraOverlap(ECardAttributeType.ShieldApplyAmount, 10, after - before);
            var frame = single.Frames[0];
            frame
                .CardUpdates[InstanceId.TryParse("target")]
                .Attributes[ECardAttributeType.ShieldApplyAmount]
                .PreviousValue = before;
            frame
                .CardUpdates[InstanceId.TryParse("target")]
                .Attributes[ECardAttributeType.ShieldApplyAmount]
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
    public void Native_latest_replay_frames_produce_twelve_ten_point_shield_gains()
    {
        // Complete native frames from the latest replay, preserving all concurrent events
        // and card/player updates. No account identifiers or snapshot names are included.
        using var resource = typeof(CombatImpactProjectorTests).Assembly.GetManifestResourceStream(
            "PostCombatImpact.Tests.Fixtures.crit-shield-tempo-20260907.frames.mpack.gz"
        )!;
        using var gzip = new System.IO.Compression.GZipStream(
            resource,
            System.IO.Compression.CompressionMode.Decompress
        );
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        var simulation = new CombatSim();
        simulation.Frames = MessagePack.MessagePackSerializer.Deserialize<List<CombatSimFrame>>(
            bytes.ToArray()
        );
        var (_, seed) = AuraOverlap(ECardAttributeType.ShieldApplyAmount, 10, 100);
        var entities = new Dictionary<string, CombatImpactEntity>
        {
            ["skl_wCm9sCM"] = seed["source"] with
            {
                Id = "skl_wCm9sCM",
                Attributes = new Dictionary<ECardAttributeType, int>
                {
                    [ECardAttributeType.Custom_0] = 10,
                },
                AbilityAttributeTypesByEffectId = new Dictionary<string, ECardAttributeType>
                {
                    ["0"] = ECardAttributeType.ShieldApplyAmount,
                },
                AbilityAttributeModifiersByEffectId = new Dictionary<
                    string,
                    TActionCardModifyAttribute
                >
                {
                    ["0"] = new()
                    {
                        AttributeType = ECardAttributeType.ShieldApplyAmount,
                        Operation = EAttributeModifierOperation.Add,
                        Value = new TReferenceValueCardAttribute
                        {
                            AttributeType = ECardAttributeType.Custom_0,
                            Target = new TTargetCardSelf(),
                        },
                    },
                },
            },
            ["itm_PO0GfJi"] = seed["target"] with
            {
                Id = "itm_PO0GfJi",
                ReferenceValuedAuraEffectIds = ["2", "4"],
                AuraAttributeModifiersByEffectId = new Dictionary<
                    string,
                    TAuraActionCardModifyAttribute
                >
                {
                    ["2"] = seed["target"].AuraAttributeModifiersByEffectId!["formula"],
                    ["4"] = seed["target"].AuraAttributeModifiersByEffectId!["formula"] with
                    {
                        AttributeType = ECardAttributeType.DamageAmount,
                    },
                },
            },
        };
        foreach (var id in new[] { "itm_SVfJncX", "itm_gCVtn1s" })
            entities[id] = new CombatImpactEntity(id, id, "Item", null, 2);
        var report = CombatImpactProjector.Project(simulation, entities);
        var skill = report.Sources.Single(source => source.Entity.Id == "skl_wCm9sCM");
        var shield = Assert.Single(skill.Groups);
        Assert.Equal(12, shield.Count);
        Assert.Equal(120, shield.ObservedValue);
        Assert.Equal(120, Assert.Single(shield.Targets).ObservedValue);
        var transitions = report
            .AttributeTransitionDiagnostics.Where(item =>
                item.NativeAttributeKey == "ShieldApplyAmount"
            )
            .ToArray();
        Assert.Equal(12, transitions.Length);
        Assert.Equal(1350, transitions.Sum(item => item.NetValue));
        Assert.Equal(1230, transitions.Sum(item => item.ResidualValue));
    }

    [Fact]
    public void All_card_attribute_gains_exclude_overlapping_aura_recalculations()
    {
        foreach (
            var attribute in new[]
            {
                ECardAttributeType.ShieldApplyAmount,
                ECardAttributeType.DamageAmount,
                ECardAttributeType.HealAmount,
                ECardAttributeType.BurnApplyAmount,
                ECardAttributeType.PoisonApplyAmount,
                ECardAttributeType.CritChance,
                ECardAttributeType.CooldownMax,
                ECardAttributeType.SellPrice,
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
            var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, amount, net);
            var report = CombatImpactProjector.Project(simulation, entities);
            Assert.Equal(amount, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
            var diagnostic = Assert.Single(report.AttributeTransitionDiagnostics);
            Assert.Equal(net, diagnostic.AttributedValue + diagnostic.ResidualValue);
        }
    }

    [Fact]
    public void Unknown_action_is_not_solved_from_a_net_delta_containing_aura_changes()
    {
        var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, 10, 130);
        entities["source"] = entities["source"] with
        {
            AbilityAttributeModifiersByEffectId = new Dictionary<string, TActionCardModifyAttribute>
            {
                ["gain"] = new()
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    Operation = EAttributeModifierOperation.Add,
                    Value = new TRangeValue { MinValue = 1, MaxValue = 200 },
                },
            },
        };
        simulation.Frames[0].Events.Add(AttributeExecution("trigger", "gain", "target"));
        entities["trigger"] = entities["source"] with
        {
            Id = "trigger",
            AbilityAttributeModifiersByEffectId = new Dictionary<string, TActionCardModifyAttribute>
            {
                ["gain"] = new()
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    Operation = EAttributeModifierOperation.Add,
                    Value = new TFixedValue { Value = 10 },
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
            var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, 10, 130);
            entities["target"] = entities["target"] with
            {
                AuraAttributeModifiersByEffectId = unknown
                    ? null
                    : new Dictionary<string, TAuraActionCardModifyAttribute>
                    {
                        ["formula"] = new()
                        {
                            AttributeType = ECardAttributeType.DamageAmount,
                            Operation = EAttributeModifierOperation.Multiply,
                            Value = new TFixedValue { Value = 2 },
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
        var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, 10, 130);
        var modifier = entities["source"].AbilityAttributeModifiersByEffectId!["gain"] with
        {
            Value = new TReferenceValueCardAttribute
            {
                AttributeType = ECardAttributeType.Custom_0,
                Target = new TTargetCardSelf(),
            },
        };
        entities["source"] = entities["source"] with
        {
            AbilityAttributeModifiersByEffectId = new Dictionary<string, TActionCardModifyAttribute>
            {
                ["gain"] = modifier,
            },
        };
        SetSourceAttributeUpdate(simulation.Frames[0], ECardAttributeType.Custom_0, 10, 20);
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Null(Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
    }

    [Fact]
    public void Hidden_self_formula_still_blocks_another_aura_from_claiming_its_delta()
    {
        var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, 10, 130);
        simulation.Frames[0].Events.RemoveAt(0);
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("source"),
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
        var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, 10, -20);
        var aura = Assert.Single(
            simulation.Frames[0].Events.OfType<CombatSimEventEffectAuraExecuted>()
        );
        aura.AppliedTo.Clear();
        aura.RemovedFrom.Add(CardTarget("target"));
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Equal(10, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
        Assert.Equal(-30, Assert.Single(report.AttributeTransitionDiagnostics).ResidualValue);
    }

    [Fact]
    public void Player_modifier_and_aura_cannot_both_claim_the_same_net_gain()
    {
        var simulation = new CombatSim();
        var frame = simulation.Frames[0];
        frame.Events.Add(
            Executed(
                "source",
                EActionCommandType.PlayerModifyAttribute,
                Player(ECombatantId.Player)
            )
        );
        frame.Events.Add(
            new CombatSimEventEffectAuraExecuted
            {
                Source = InstanceId.TryParse("trigger"),
                AppliedTo = { Player(ECombatantId.Player) },
            }
        );
        frame.PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.HealthMax] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthMax,
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
    public void Aura_metadata_reader_rejects_conflicting_effect_ids()
    {
        var additive = new TCardAura
        {
            Id = "formula",
            Action = new TAuraActionCardModifyAttribute
            {
                AttributeType = ECardAttributeType.ShieldApplyAmount,
                Operation = EAttributeModifierOperation.Add,
                Value = new TFixedValue { Value = 10 },
            },
        };
        Assert.Single(CombatImpactAbilityAttributeModifierReader.ReadAuras([additive, additive])!);
        var conflicting = additive with
        {
            Action = new TAuraActionCardModifyAttribute
            {
                AttributeType = ECardAttributeType.ShieldApplyAmount,
                Operation = EAttributeModifierOperation.Multiply,
                Value = new TFixedValue { Value = 2 },
            },
        };
        Assert.Null(
            CombatImpactAbilityAttributeModifierReader.ReadAuras([additive, conflicting, additive])
        );
    }

    [Fact]
    public void Aura_on_an_unrelated_attribute_does_not_hide_the_explicit_gain()
    {
        var (simulation, entities) = AuraOverlap(ECardAttributeType.DamageAmount, 10, 10);
        entities["target"] = entities["target"] with
        {
            AuraAttributeModifiersByEffectId = new Dictionary<
                string,
                TAuraActionCardModifyAttribute
            >
            {
                ["formula"] = new()
                {
                    AttributeType = ECardAttributeType.ShieldApplyAmount,
                    Operation = EAttributeModifierOperation.Add,
                    Value = new TFixedValue { Value = 100 },
                },
            },
        };
        var report = CombatImpactProjector.Project(simulation, entities);
        Assert.Equal(10, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
    }

    private static (CombatSim, Dictionary<string, CombatImpactEntity>) AuraOverlap(
        ECardAttributeType attribute,
        int amount,
        int net
    )
    {
        var simulation = new CombatSim();
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = AttributeModifierEntity(
            "source",
            "Skill",
            "gain",
            new TActionCardModifyAttribute
            {
                AttributeType = attribute,
                Operation = EAttributeModifierOperation.Add,
                Value = new TFixedValue { Value = amount },
            },
            0
        );
        entities["target"] = entities["target"] with
        {
            ReferenceValuedAuraEffectIds = ["formula"],
            AuraAttributeTypesByEffectId = new Dictionary<string, ECardAttributeType>
            {
                ["formula"] = attribute,
            },
            AuraAttributeModifiersByEffectId = new Dictionary<
                string,
                TAuraActionCardModifyAttribute
            >
            {
                ["formula"] = new()
                {
                    AttributeType = attribute,
                    Operation = EAttributeModifierOperation.Add,
                    Value = new TReferenceValuePlayerAttribute
                    {
                        AttributeType = EPlayerAttributeType.Tempo,
                    },
                },
            },
        };
        var frame = simulation.Frames[0];
        frame.Events.Add(AttributeExecution("source", "gain", "target"));
        frame.Events.Add(
            new CombatSimEventEffectAuraExecuted
            {
                Source = InstanceId.TryParse("target"),
                EffectId = "formula",
                AppliedTo = { CardTarget("target") },
            }
        );
        frame.CardUpdates[InstanceId.TryParse("target")] = new CombatSimCardUpdate
        {
            CardInstanceId = InstanceId.TryParse("target"),
            Attributes =
            {
                [attribute] = new CombatSimCardAttributeUpdate
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
