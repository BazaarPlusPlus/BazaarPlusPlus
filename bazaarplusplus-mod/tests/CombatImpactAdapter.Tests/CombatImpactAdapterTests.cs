using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.CombatSimulation;
using Xunit;

namespace CombatImpactAdapter.Tests;

/// <summary>
/// The desktop host's Combat Impact adapter (ADR-0019): the game's combat simulation copied into
/// Core's combat timeline, and the producer run on native replay frames through it.
/// </summary>
public sealed partial class CombatImpactAdapterTests
{
    [Fact]
    public void Native_latest_replay_frames_produce_twelve_ten_point_shield_gains()
    {
        // Complete native frames from the latest replay, preserving all concurrent events
        // and card/player updates. No account identifiers or snapshot names are included.
        using var resource = typeof(CombatImpactAdapterTests).Assembly.GetManifestResourceStream(
            "CombatImpactAdapter.Tests.Fixtures.crit-shield-tempo-20260907.frames.mpack.gz"
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
        var formula = new CombatAttributeModifier
        {
            AttributeType = CombatCardAttribute.ShieldApplyAmount,
            Operation = CombatAttributeOperation.Add,
            Value = new CombatOpaqueReference { Kind = CombatOpaqueReferenceKind.PlayerAttribute },
        };
        var entities = new Dictionary<string, CombatImpactEntity>
        {
            ["skl_wCm9sCM"] = new(
                "skl_wCm9sCM",
                "Skill",
                "Skill",
                null,
                0,
                Attributes: new Dictionary<CombatCardAttribute, int>
                {
                    [CombatCardAttribute.Custom_0] = 10,
                },
                AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
                {
                    ["0"] = CombatCardAttribute.ShieldApplyAmount,
                },
                AbilityAttributeModifiersByEffectId: new Dictionary<string, CombatAttributeModifier>
                {
                    ["0"] = new()
                    {
                        AttributeType = CombatCardAttribute.ShieldApplyAmount,
                        Operation = CombatAttributeOperation.Add,
                        Value = new CombatCardAttributeReference
                        {
                            AttributeType = CombatCardAttribute.Custom_0,
                            TargetsSelf = true,
                        },
                    },
                }
            ),
            ["itm_PO0GfJi"] = new(
                "itm_PO0GfJi",
                "Bread Knife",
                "Item",
                null,
                1,
                AuraAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
                {
                    ["formula"] = CombatCardAttribute.ShieldApplyAmount,
                },
                ReferenceValuedAuraEffectIds: ["2", "4"],
                AuraAttributeModifiersByEffectId: new Dictionary<string, CombatAttributeModifier>
                {
                    ["2"] = formula,
                    ["4"] = formula with { AttributeType = CombatCardAttribute.DamageAmount },
                }
            ),
        };
        foreach (var id in new[] { "itm_SVfJncX", "itm_gCVtn1s" })
            entities[id] = new CombatImpactEntity(id, id, "Item", null, 2);
        var report = CombatImpactProjector.Project(
            DesktopCombatTimeline.From(simulation),
            entities
        );
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
    public void Card_action_cost_event_is_read_without_a_compile_time_runtime_type_dependency()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                OptionalCombatSimEventFactory.CardActionCostSpent(
                    "source",
                    EPlayerAttributeType.Tempo,
                    ECardAttributeType.TempoCost
                )
            );

        var timeline = DesktopCombatTimeline.From(simulation);

        var spent = Assert.IsType<CombatCardActionCostSpent>(
            Assert.Single(Assert.Single(timeline.Frames).Events)
        );
        Assert.Equal("source", spent.ExecutingCard);
        Assert.Equal(CombatPlayerAttribute.Tempo, spent.PlayerAttributeSpent);
    }

    [Fact]
    public void Condition_observations_without_an_executed_effect_are_not_copied()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectTriggered
                {
                    ExecutionContextId = "observed-context",
                    EffectId = "freeze-effect",
                    Source = InstanceId.TryParse("source"),
                    TriggerSource = InstanceId.TryParse("trigger"),
                    Targets = { CardTarget("target") },
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectExecuted
                {
                    ExecutionContextId = "executed-context",
                    EffectId = "freeze-effect",
                    ActionType = EActionCommandType.CardFreeze,
                    Source = InstanceId.TryParse("source"),
                    TriggerSource = InstanceId.TryParse("trigger"),
                    Target = CardTarget("target"),
                }
            );

        var executed = Assert.IsType<CombatEffectExecuted>(
            Assert.Single(Assert.Single(DesktopCombatTimeline.From(simulation).Frames).Events)
        );
        Assert.Equal("executed-context", executed.ExecutionContextId);
        Assert.Equal(CombatActionType.CardFreeze, executed.ActionType);
        Assert.Equal("source", executed.Source);
        Assert.Equal("trigger", executed.TriggerSource);
        Assert.Equal("target", Assert.IsType<CombatCardTarget>(executed.Target).Target);
    }

    [Fact]
    public void Self_card_attribute_reference_keeps_its_target_and_modifier()
    {
        var modifier = DesktopCombatTimeline.ToModifier(
            new TAuraActionCardModifyAttribute
            {
                AttributeType = ECardAttributeType.DamageAmount,
                Operation = EAttributeModifierOperation.Add,
                Value = new TReferenceValueCardAttribute
                {
                    AttributeType = ECardAttributeType.Custom_0,
                    Target = new TTargetCardSelf(),
                    Modifier = new TValueModifier
                    {
                        ModifyMode = EValueModifierMode.Multiply,
                        Value = new TFixedValue { Value = 2 },
                    },
                },
            }
        );

        Assert.Equal(
            new CombatAttributeModifier
            {
                AttributeType = CombatCardAttribute.DamageAmount,
                Operation = CombatAttributeOperation.Add,
                Value = new CombatCardAttributeReference
                {
                    AttributeType = CombatCardAttribute.Custom_0,
                    TargetsSelf = true,
                    Modifier = new CombatValueModifier
                    {
                        ModifyMode = CombatValueModifierMode.Multiply,
                        Value = new CombatFixedValue { Value = 2 },
                    },
                },
            },
            modifier
        );
    }

    private static EffectTargetCard CardTarget(string id) =>
        new() { Target = InstanceId.TryParse(id) };

    private static IReadOnlyDictionary<string, CombatImpactEntity> Entities() =>
        new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["source"] = new("source", "Fairies", "Skill", null, 0, CombatantId: Combatant.Player),
            ["trigger"] = new("trigger", "Trigger Item", "Item", null, 1),
            ["effect"] = new("effect", "Effect", "Effect", null, 2),
            ["target"] = new("target", "Bread Knife", "Item", null, 1),
            ["target-2"] = new("target-2", "The Eclipse", "Item", null, 2),
            [CombatImpactProjector.PlayerId(Combatant.Player)] = new(
                CombatImpactProjector.PlayerId(Combatant.Player),
                "You",
                "Hero",
                null,
                3
            ),
            [CombatImpactProjector.PlayerId(Combatant.Opponent)] = new(
                CombatImpactProjector.PlayerId(Combatant.Opponent),
                "Opponent",
                "Hero",
                null,
                4
            ),
        };
}
