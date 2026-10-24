using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed partial class CombatImpactProjectorTests
{
    [Fact]
    public void F_minor_reconstructs_tempo_from_haste_item_uses_on_the_f_note()
    {
        var skillTemplateId = Guid.Parse("37251594-5ff0-4604-804e-7259ee666f60");
        var sourceRule = new CombatImpactPrerequisiteSkillSourceRule(
            "minor-tempo",
            skillTemplateId,
            [CardTier.Gold]
        );
        var simulation = new CombatTimeline();
        simulation.CardStats["turner"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 7,
        };
        simulation.CardStats["non-haste-on-note"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 20,
        };
        simulation.CardStats["haste-off-note"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 30,
        };
        simulation.CardStats["f-minor"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.TempoAdded] = 14,
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["f-minor"] = new CombatImpactEntity(
            "f-minor",
            "F Minor",
            "Skill",
            null,
            5,
            skillTemplateId,
            CardTier.Gold,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.Custom_0] = 2,
            },
            CombatantId: Combatant.Player
        );
        entities["f-note"] = new CombatImpactEntity(
            "f-note",
            "[F Note] Socket Effect",
            "SocketEffect",
            null,
            6,
            Guid.Parse("04eca54a-69bf-4874-8b6d-56d284bb58be"),
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_6,
            Section: CardSection.Hand,
            PrerequisiteSkillSourceRulesByEffectId: new Dictionary<
                string,
                CombatImpactPrerequisiteSkillSourceRule
            >
            {
                [sourceRule.EffectId] = sourceRule,
            },
            UseAttributionRules:
            [
                new CombatImpactUseAttributionRule(
                    sourceRule,
                    new CombatImpactItemTagCondition(
                        CombatListComparison.Any,
                        HiddenTags: [CardHiddenTag.Haste]
                    ),
                    2
                ),
            ]
        );
        entities["turner"] = new CombatImpactEntity(
            "turner",
            "Turner",
            "Item",
            null,
            7,
            DisplaySpan: 2,
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_5,
            HiddenTags: new[] { CardHiddenTag.Haste },
            Section: CardSection.Hand
        );
        entities["non-haste-on-note"] = new CombatImpactEntity(
            "non-haste-on-note",
            "Non-Haste Item",
            "Item",
            null,
            8,
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_6,
            Section: CardSection.Hand
        );
        entities["haste-off-note"] = new CombatImpactEntity(
            "haste-off-note",
            "Off-Note Haste Item",
            "Item",
            null,
            9,
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_7,
            HiddenTags: new[] { CardHiddenTag.Haste },
            Section: CardSection.Hand
        );

        var source = Assert.Single(
            CombatImpactProjector.Project(simulation, entities).Sources,
            candidate => candidate.Entity.Id == "f-minor"
        );
        var tempo = Assert.Single(source.Groups);

        Assert.Equal(7, source.EffectCount);
        Assert.Equal(7, tempo.Count);
        Assert.Equal(14, tempo.ObservedValue);
        Assert.Equal("TempoApplyAmount", tempo.NativeAttributeKey);
        Assert.Equal(CombatImpactEventSurface.AppliedEffect, tempo.Surface);
        Assert.Equal(CombatImpactOccurrenceBasis.ReconstructedTransition, tempo.OccurrenceBasis);
        Assert.Equal(14, tempo.AuthoritativeMetric?.Value);
    }

    [Fact]
    public void A_minor_reconstructs_tempo_from_public_weapon_tags_without_card_specific_logic()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 4,
        };
        var entities = TempoAttributionEntities(
            new CombatImpactItemTagCondition(CombatListComparison.Any, PublicTags: [CardTag.Weapon])
        );

        var source = Assert.Single(
            CombatImpactProjector.Project(simulation, entities).Sources,
            candidate => candidate.Entity.Id == "minor-skill"
        );
        var tempo = Assert.Single(source.Groups);

        Assert.Equal(4, source.EffectCount);
        Assert.Equal(8, tempo.ObservedValue);
        Assert.Equal("TempoApplyAmount", tempo.NativeAttributeKey);
        Assert.Equal("qualifying-item", Assert.Single(tempo.TriggerSources).Entity.Id);
    }

    [Fact]
    public void A_minor_without_an_a_note_does_not_reconstruct_weapon_uses()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 4,
        };
        var entities = TempoAttributionEntities(
            new CombatImpactItemTagCondition(CombatListComparison.Any, PublicTags: [CardTag.Weapon])
        );
        entities.Remove("note-effect");

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "minor-skill");
        Assert.Empty(report.ProjectionDiagnostics);
    }

    [Fact]
    public void Trigger_item_tempo_metric_stays_with_the_item_instead_of_moving_to_the_skill()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 2,
            [CombatCardStat.TempoAdded] = 9,
        };
        var report = CombatImpactProjector.Project(
            simulation,
            TempoAttributionEntities(
                new CombatImpactItemTagCondition(
                    CombatListComparison.Any,
                    PublicTags: [CardTag.Weapon]
                )
            )
        );

        var skillTempo = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "minor-skill").Groups
        );
        var itemTempo = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "qualifying-item").Groups
        );

        Assert.Equal(4, skillTempo.ObservedValue);
        Assert.Null(skillTempo.AuthoritativeMetric);
        Assert.Null(itemTempo.ObservedValue);
        Assert.Equal(9, itemTempo.AuthoritativeMetric?.Value);
    }

    [Fact]
    public void Socket_effect_authoritative_metric_is_diagnosed_without_moving_it_to_the_skill()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 1,
        };
        simulation.CardStats["note-effect"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.TempoAdded] = 2,
        };
        var report = CombatImpactProjector.Project(
            simulation,
            TempoAttributionEntities(
                new CombatImpactItemTagCondition(
                    CombatListComparison.Any,
                    PublicTags: [CardTag.Weapon]
                )
            )
        );

        var skillTempo = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "minor-skill").Groups
        );

        Assert.Equal(2, skillTempo.ObservedValue);
        Assert.Null(skillTempo.AuthoritativeMetric);
        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "note-effect");
        Assert.Contains(
            report.ProjectionDiagnostics,
            diagnostic =>
                diagnostic.Kind
                    == CombatImpactProjectionDiagnosticKind.NonDisplayableAuthoritativeMetric
                && diagnostic.SourceId == "note-effect"
                && diagnostic.EffectId == "TempoApplyAmount"
        );
    }

    [Fact]
    public void Explicit_minor_tempo_execution_is_subtracted_before_use_count_reconstruction()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 3,
        };
        var explicitTempo = Executed(
            "note-effect",
            CombatActionType.PlayerModifyAttribute,
            Player(Combatant.Player),
            triggerSource: "qualifying-item"
        );
        explicitTempo.EffectId = "minor-tempo";
        simulation.Frames[0].Events.Add(explicitTempo);

        var report = CombatImpactProjector.Project(
            simulation,
            TempoAttributionEntities(
                new CombatImpactItemTagCondition(
                    CombatListComparison.Any,
                    PublicTags: [CardTag.Weapon]
                )
            )
        );
        var source = Assert.Single(
            report.Sources,
            candidate => candidate.Entity.Id == "minor-skill"
        );
        var tempo = Assert.Single(source.Groups);

        Assert.Equal(3, tempo.Count);
        Assert.Equal(6, tempo.ObservedValue);
        Assert.Empty(report.ProjectionDiagnostics);
    }

    [Fact]
    public void Explicit_tempo_action_uses_the_same_reconciliation()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 2,
        };
        var explicitTempo = Executed(
            "note-effect",
            CombatActionType.PlayerTempoApply,
            Player(Combatant.Player),
            triggerSource: "qualifying-item"
        );
        explicitTempo.EffectId = "minor-tempo";
        simulation.Frames[0].Events.Add(explicitTempo);

        var report = CombatImpactProjector.Project(
            simulation,
            TempoAttributionEntities(
                new CombatImpactItemTagCondition(
                    CombatListComparison.Any,
                    PublicTags: [CardTag.Weapon]
                )
            )
        );
        var tempo = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "minor-skill").Groups
        );

        Assert.Equal(2, tempo.Count);
        Assert.Equal(4, tempo.ObservedValue);
        Assert.Empty(report.ProjectionDiagnostics);
    }

    [Fact]
    public void Duplicate_use_rules_keep_observed_source_mapping_without_reconstruction()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 2,
        };
        var explicitTempo = Executed(
            "note-effect",
            CombatActionType.PlayerModifyAttribute,
            Player(Combatant.Player),
            triggerSource: "qualifying-item"
        );
        explicitTempo.EffectId = "minor-tempo";
        simulation.Frames[0].Events.Add(explicitTempo);
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Tempo] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Tempo,
                    PreviousValue = 0,
                    CurrentValue = 2,
                },
            },
        };
        var entities = TempoAttributionEntities(
            new CombatImpactItemTagCondition(CombatListComparison.Any, PublicTags: [CardTag.Weapon])
        );
        var rule = Assert.Single(entities["note-effect"].UseAttributionRules!);
        entities["note-effect"] = entities["note-effect"] with
        {
            UseAttributionRules = [rule, rule],
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var tempo = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "minor-skill").Groups
        );

        Assert.Equal(1, tempo.Count);
        Assert.Equal(2, tempo.ObservedValue);
        Assert.Equal(CombatImpactOccurrenceBasis.ExplicitExecution, tempo.OccurrenceBasis);
    }

    [Fact]
    public void Rule_execution_mismatch_blocks_reconstruction_instead_of_double_counting()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 3,
        };
        var mismatched = Executed(
            "note-effect",
            CombatActionType.CardHaste,
            CardTarget("qualifying-item"),
            triggerSource: "qualifying-item"
        );
        mismatched.EffectId = "minor-tempo";
        simulation.Frames[0].Events.Add(mismatched);

        var report = CombatImpactProjector.Project(
            simulation,
            TempoAttributionEntities(
                new CombatImpactItemTagCondition(
                    CombatListComparison.Any,
                    PublicTags: [CardTag.Weapon]
                )
            )
        );

        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "minor-skill");
        Assert.Contains(
            report.ProjectionDiagnostics,
            diagnostic =>
                diagnostic.Kind == CombatImpactProjectionDiagnosticKind.RuleExecutionMismatch
        );
    }

    [Fact]
    public void Prerequisite_source_resolution_is_scoped_to_the_implementation_owner()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 2,
        };
        var entities = TempoAttributionEntities(
            new CombatImpactItemTagCondition(CombatListComparison.Any, PublicTags: [CardTag.Weapon])
        );
        entities["opponent-minor"] = entities["minor-skill"] with
        {
            Id = "opponent-minor",
            CombatantId = Combatant.Opponent,
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.Equal("minor-skill", Assert.Single(report.Sources).Entity.Id);
        Assert.Empty(report.ProjectionDiagnostics);
    }

    [Fact]
    public void Duplicate_same_owner_prerequisite_skills_block_attribution()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 2,
        };
        var entities = TempoAttributionEntities(
            new CombatImpactItemTagCondition(CombatListComparison.Any, PublicTags: [CardTag.Weapon])
        );
        entities["duplicate-minor"] = entities["minor-skill"] with
        {
            Id = "duplicate-minor",
            Order = 8,
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.Empty(report.Sources);
        Assert.Contains(
            report.ProjectionDiagnostics,
            diagnostic =>
                diagnostic.Kind == CombatImpactProjectionDiagnosticKind.AmbiguousPrerequisiteSkill
        );
    }

    [Fact]
    public void Stash_item_with_the_same_socket_number_is_not_reconstructed()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["stash-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 9,
        };
        var entities = TempoAttributionEntities(
            new CombatImpactItemTagCondition(CombatListComparison.Any, PublicTags: [CardTag.Weapon])
        );
        entities.Remove("qualifying-item");
        entities["stash-item"] = new CombatImpactEntity(
            "stash-item",
            "Stashed Weapon",
            "Item",
            null,
            9,
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_6,
            Section: CardSection.Stash,
            Tags: [CardTag.Weapon]
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.Empty(report.Sources);
        Assert.Empty(report.ProjectionDiagnostics);
    }

    [Fact]
    public void Explicit_applications_over_use_count_are_reported_without_balancing_entries()
    {
        var simulation = new CombatTimeline();
        simulation.CardStats["qualifying-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 1,
        };
        for (var index = 0; index < 2; index++)
        {
            var explicitTempo = Executed(
                "note-effect",
                CombatActionType.PlayerModifyAttribute,
                Player(Combatant.Player),
                triggerSource: "qualifying-item"
            );
            explicitTempo.EffectId = "minor-tempo";
            simulation.Frames[0].Events.Add(explicitTempo);
        }

        var report = CombatImpactProjector.Project(
            simulation,
            TempoAttributionEntities(
                new CombatImpactItemTagCondition(
                    CombatListComparison.Any,
                    PublicTags: [CardTag.Weapon]
                )
            )
        );
        var group = Assert.Single(Assert.Single(report.Sources).Groups);

        Assert.Equal(2, group.Count);
        Assert.Contains(
            report.ProjectionDiagnostics,
            diagnostic =>
                diagnostic.Kind
                == CombatImpactProjectionDiagnosticKind.ExplicitApplicationsExceedUseCount
        );
    }

    [Fact]
    public void Prerequisite_gated_aura_is_attributed_to_the_unique_visible_skill()
    {
        var skillTemplateId = Guid.Parse("0ab8bd31-0655-4f18-844f-844e1452942d");
        var sourceRule = new CombatImpactPrerequisiteSkillSourceRule(
            "major-multicast",
            skillTemplateId,
            []
        );
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["major-skill"] = new CombatImpactEntity(
            "major-skill",
            "A Major",
            "Skill",
            null,
            5,
            skillTemplateId,
            CombatantId: Combatant.Player
        );
        entities["note-effect"] = new CombatImpactEntity(
            "note-effect",
            "A Note Socket Effect",
            "SocketEffect",
            null,
            6,
            CombatantId: Combatant.Player,
            AuraAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                [sourceRule.EffectId] = CombatCardAttribute.Multicast,
            },
            PrerequisiteSkillSourceRulesByEffectId: new Dictionary<
                string,
                CombatImpactPrerequisiteSkillSourceRule
            >
            {
                [sourceRule.EffectId] = sourceRule,
            }
        );
        entities["target"] = entities["target"] with { CombatantId = Combatant.Player };
        var target = ("target");
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    EffectId = sourceRule.EffectId,
                    Source = ("note-effect"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.Multicast] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.Multicast,
                    PreviousValue = 1,
                    CurrentValue = 2,
                },
            },
        };

        var source = Assert.Single(
            CombatImpactProjector.Project(simulation, entities).Sources,
            candidate => candidate.Entity.Id == "major-skill"
        );

        Assert.Equal("Multicast", Assert.Single(source.Groups).NativeAttributeKey);
    }

    [Fact]
    public void Visible_item_aura_keeps_its_direct_source_even_when_a_skill_is_a_prerequisite()
    {
        var skillTemplateId = Guid.NewGuid();
        var sourceRule = new CombatImpactPrerequisiteSkillSourceRule(
            "unlocked-aura",
            skillTemplateId,
            []
        );
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["unlock-skill"] = new CombatImpactEntity(
            "unlock-skill",
            "Unlock Skill",
            "Skill",
            null,
            5,
            skillTemplateId,
            CombatantId: Combatant.Player
        );
        entities["visible-item"] = new CombatImpactEntity(
            "visible-item",
            "Visible Item",
            "Item",
            null,
            6,
            CombatantId: Combatant.Player,
            AuraAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                [sourceRule.EffectId] = CombatCardAttribute.DamageAmount,
            },
            PrerequisiteSkillSourceRulesByEffectId: new Dictionary<
                string,
                CombatImpactPrerequisiteSkillSourceRule
            >
            {
                [sourceRule.EffectId] = sourceRule,
            }
        );
        var target = ("target");
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    EffectId = sourceRule.EffectId,
                    Source = ("visible-item"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 10,
                    CurrentValue = 20,
                },
            },
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, entities).Sources);

        Assert.Equal("visible-item", source.Entity.Id);
        Assert.Equal("DamageAmount", Assert.Single(source.Groups).NativeAttributeKey);
    }

    [Fact]
    public void Direct_damage_is_a_successful_mapping_despite_its_zero_kind_value()
    {
        Assert.True(
            CombatImpactProjector.TryResolveKind(CombatActionType.PlayerDamage, out var kind)
        );
        Assert.Equal(CombatImpactKind.DirectDamage, kind);
    }

    [Theory]
    [InlineData(CombatActionType.None)]
    [InlineData((CombatActionType)int.MaxValue)]
    public void Unmapped_actions_return_false_and_the_default_kind(CombatActionType action)
    {
        Assert.False(CombatImpactProjector.TryResolveKind(action, out var kind));
        Assert.Equal(default, kind);
    }

    [Fact]
    public void Every_native_action_has_an_explicit_display_or_ignore_classification()
    {
        Assert.All(
            Enum.GetValues<CombatActionType>(),
            action =>
                Assert.True(
                    CombatImpactProjector.HasExplicitDisplayClassification(action),
                    $"{action} must be explicitly displayed or ignored."
                )
        );
    }

    [Theory]
    [InlineData(CombatActionType.CardForceUse, "ForceUseTargets")]
    [InlineData(CombatActionType.CardEnchant, "EnchantTargets")]
    [InlineData(CombatActionType.CardEnchantRemove, "EnchantRemoveTargets")]
    [InlineData(CombatActionType.CardTransform, "TransformTargets")]
    [InlineData(CombatActionType.CardTransformDestroyed, "TransformTargets")]
    [InlineData(CombatActionType.CardUpgrade, "UpgradeTargets")]
    [InlineData(CombatActionType.CardRepair, "RepairTargets")]
    public void Projects_player_visible_categorical_card_actions_as_counted_impacts(
        CombatActionType action,
        string expectedKey
    )
    {
        var simulation = new CombatTimeline();
        simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));

        var report = CombatImpactProjector.Project(simulation, Entities());
        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        var target = Assert.Single(caused.Targets);
        var received = Assert.Single(Assert.Single(report.Received).Groups);
        var incoming = Assert.Single(received.Sources);

        Assert.Equal(CombatImpactKind.AttributeChange, caused.Kind);
        Assert.Equal(CombatImpactEventSurface.AppliedEffect, caused.Surface);
        Assert.Equal(expectedKey, caused.NativeAttributeKey);
        Assert.Equal(1, caused.Count);
        Assert.Null(caused.ObservedValue);
        Assert.Equal("×1", CombatImpactMetricFormatter.Group(caused, chinese: false));
        Assert.Equal("×1", CombatImpactMetricFormatter.Target(caused, target, chinese: false));
        Assert.Equal("×1", CombatImpactMetricFormatter.IncomingGroup(received, chinese: false));
        Assert.Equal(
            "×1",
            CombatImpactMetricFormatter.IncomingSource(received, incoming, chinese: false)
        );
    }

    [Fact]
    public void Transformed_source_impacts_and_stats_roll_up_to_the_visible_original_item()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatCardTransformed
                {
                    OriginalInstanceId = "original-item",
                    TransformedCardInstanceIds = ["transformed-item"],
                }
            );
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[1]
            .Events.Add(
                Executed("transformed-item", CombatActionType.CardFreeze, CardTarget("target"))
            );
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[2]
            .Events.Add(
                new CombatCardTransformReverted
                {
                    OriginalInstanceId = "original-item",
                    TransformedCardInstanceIds = ["transformed-item"],
                }
            );
        // Real replay payloads keep these as disjoint per-incarnation accounting windows.
        simulation.CardStats["original-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 1,
            [CombatCardStat.FrozenCardsCount] = 1,
        };
        simulation.CardStats["transformed-item"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 2,
            [CombatCardStat.FrozenCardsCount] = 2,
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["original-item"] = new CombatImpactEntity(
            "original-item",
            "Original Item",
            "Item",
            null,
            5,
            CombatantId: Combatant.Player
        );
        entities["transformed-item"] = new CombatImpactEntity(
            "transformed-item",
            "Temporary Form",
            "Item",
            null,
            6,
            CombatantId: Combatant.Player
        );

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(
            report.Sources,
            candidate => candidate.Entity.Id == "original-item"
        );
        var freeze = Assert.Single(source.Groups);

        Assert.DoesNotContain(
            report.Sources,
            candidate => candidate.Entity.Id == "transformed-item"
        );
        Assert.Equal(3, source.UseCount);
        Assert.Equal(3, freeze.AuthoritativeMetric?.Value);
        Assert.Equal("target", Assert.Single(freeze.Targets).Entity.Id);
    }

    [Fact]
    public void Nested_transforms_and_transformed_targets_resolve_to_the_highest_visible_ancestor()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(Transformed("original-target", "middle-target", Combatant.Opponent));
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[1]
            .Events.Add(Transformed("middle-target", "final-target", Combatant.Opponent));
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[2]
            .Events.Add(Executed("source", CombatActionType.CardSlow, CardTarget("final-target")));
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["original-target"] = new CombatImpactEntity(
            "original-target",
            "Original Target",
            "Item",
            null,
            5,
            CombatantId: Combatant.Opponent
        );
        entities["middle-target"] = new CombatImpactEntity(
            "middle-target",
            "Middle Target",
            "Item",
            null,
            6,
            CombatantId: Combatant.Opponent
        );
        entities["final-target"] = new CombatImpactEntity(
            "final-target",
            "Final Target",
            "Item",
            null,
            7,
            CombatantId: Combatant.Opponent
        );

        var report = CombatImpactProjector.Project(simulation, entities);
        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        var target = Assert.Single(group.Targets);

        Assert.Equal("original-target", target.Entity.Id);
        Assert.Equal("original-target", Assert.Single(report.Received).Entity.Id);
    }

    [Fact]
    public void Malformed_transform_cycles_still_fold_to_one_deterministic_entity()
    {
        var simulation = new CombatTimeline();
        simulation.Frames[0].Events.Add(Transformed("b-item", "a-item", Combatant.Player));
        simulation.Frames.Add(new CombatFrame());
        simulation.Frames[1].Events.Add(Transformed("a-item", "b-item", Combatant.Player));
        simulation.Frames.Add(new CombatFrame());
        simulation.Frames[2].Events.Add(Transformed("a-item", "c-item", Combatant.Player));
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[3]
            .Events.Add(Executed("a-item", CombatActionType.CardFreeze, CardTarget("target")));
        simulation
            .Frames[3]
            .Events.Add(Executed("b-item", CombatActionType.CardFreeze, CardTarget("target")));
        simulation
            .Frames[3]
            .Events.Add(Executed("c-item", CombatActionType.CardFreeze, CardTarget("target")));
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["a-item"] = new CombatImpactEntity(
            "a-item",
            "A Item",
            "Item",
            null,
            5,
            CombatantId: Combatant.Player
        );
        entities["b-item"] = new CombatImpactEntity(
            "b-item",
            "B Item",
            "Item",
            null,
            6,
            CombatantId: Combatant.Player
        );
        entities["c-item"] = new CombatImpactEntity(
            "c-item",
            "C Item",
            "Item",
            null,
            7,
            CombatantId: Combatant.Player
        );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, entities).Sources);

        Assert.Equal("a-item", source.Entity.Id);
        Assert.Equal(3, Assert.Single(source.Groups).Count);
    }

    [Fact]
    public void Enchant_actions_preserve_the_unique_applied_enchantment_type()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardEnchant, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(
                new CombatCardEnchanted
                {
                    InstanceId = "target",
                    EnchantmentType = CardEnchantment.Fiery,
                }
            );

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("EnchantTargets:Fiery", group.NativeAttributeKey);
        Assert.Equal(1, group.Count);
    }

    [Fact]
    public void Experimental_chef_projects_both_enchantments_from_the_recorded_event_shape()
    {
        const string chef = "skl_RIsKVL3";
        const string caviar = "itm_UCdcbhQ";
        const string pizzaCutter = "itm_68Nys1j";
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(Executed(chef, CombatActionType.CardEnchant, CardTarget(caviar)));
        simulation
            .Frames[0]
            .Events.Add(
                new CombatCardEnchanted
                {
                    InstanceId = caviar,
                    EnchantmentType = CardEnchantment.Fiery,
                }
            );
        simulation
            .Frames[0]
            .Events.Add(Executed(chef, CombatActionType.CardEnchant, CardTarget(pizzaCutter)));
        simulation
            .Frames[0]
            .Events.Add(
                new CombatCardEnchanted
                {
                    InstanceId = pizzaCutter,
                    EnchantmentType = CardEnchantment.Shiny,
                }
            );
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            [chef] = new(chef, "Experimental Chef", "Skill", null, 0),
            [caviar] = new(caviar, "Caviar", "Item", null, 1),
            [pizzaCutter] = new(pizzaCutter, "Pizza Cutter", "Item", null, 2),
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(report.Sources);
        var groups = source.Groups.ToDictionary(group => group.NativeAttributeKey);

        Assert.Equal("Experimental Chef", source.Entity.Name);
        Assert.Equal(2, source.EffectCount);
        Assert.Equal("Caviar", Assert.Single(groups["EnchantTargets:Fiery"].Targets).Entity.Name);
        Assert.Equal(
            "Pizza Cutter",
            Assert.Single(groups["EnchantTargets:Shiny"].Targets).Entity.Name
        );
        Assert.Collection(
            report.Received.OrderBy(received => received.Entity.Order),
            received =>
            {
                Assert.Equal("Caviar", received.Entity.Name);
                Assert.Equal(1, received.EffectCount);
            },
            received =>
            {
                Assert.Equal("Pizza Cutter", received.Entity.Name);
                Assert.Equal(1, received.EffectCount);
            }
        );
    }

    [Theory]
    [InlineData(CombatActionType.PlayerBurnRemove, CombatPlayerAttribute.Burn, "BurnRemoveAmount")]
    [InlineData(
        CombatActionType.PlayerPoisonRemove,
        CombatPlayerAttribute.Poison,
        "PoisonRemoveAmount"
    )]
    [InlineData(
        CombatActionType.PlayerRegenRemove,
        CombatPlayerAttribute.HealthRegen,
        "RegenRemoveAmount"
    )]
    [InlineData(CombatActionType.PlayerRageRemove, CombatPlayerAttribute.Rage, "RageRemoveAmount")]
    public void Player_status_removals_use_positive_removed_amounts_when_uniquely_attributed(
        CombatActionType action,
        CombatPlayerAttribute attribute,
        string expectedKey
    )
    {
        var simulation = new CombatTimeline();
        simulation.Frames[0].Events.Add(Executed("source", action, Player(Combatant.Player)));
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [attribute] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = 10,
                    CurrentValue = 4,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(expectedKey, group.NativeAttributeKey);
        Assert.Equal(1, group.Count);
        Assert.Equal(6, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.LowerBound, group.ObservedCoverage);
        Assert.Equal("×1 · 6", CombatImpactMetricFormatter.Group(group, chinese: false));
    }

    [Fact]
    public void Shield_removal_uses_the_exact_negative_shield_adjustment_as_a_positive_amount()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerShieldRemove, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Shield,
                    AttributeChanged = CombatHealthChange.Shield,
                    Amount = -9,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("ShieldRemoveAmount", group.NativeAttributeKey);
        Assert.Equal(9, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, group.ObservedCoverage);
        Assert.Equal("×1 · 9", CombatImpactMetricFormatter.Group(group, chinese: false));
    }

    [Fact]
    public void Ambiguous_same_frame_status_removals_keep_counts_and_omit_the_amount()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerBurnRemove, Player(Combatant.Player))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", CombatActionType.PlayerBurnRemove, Player(Combatant.Player))
            );
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Burn] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Burn,
                    PreviousValue = 10,
                    CurrentValue = 4,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var groups = report.Sources.SelectMany(source => source.Groups).ToArray();

        Assert.Equal(2, groups.Length);
        Assert.All(groups, group => Assert.Null(group.ObservedValue));
        Assert.All(
            groups,
            group => Assert.Equal("×1", CombatImpactMetricFormatter.Group(group, chinese: false))
        );
    }

    [Fact]
    public void Projects_uniquely_attributed_damage_and_derived_critical_presentation()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Shield,
                    Amount = -60,
                    IsCrit = true,
                },
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -100,
                    IsCrit = false,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 160,
            [CombatCardStat.UseCount] = 1,
        };

        var report = CombatImpactProjector.Project(simulation, Entities());

        var source = Assert.Single(report.Sources);
        Assert.Equal(1, source.UseCount);
        var damage = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.DirectDamage
        );
        Assert.Equal(160, damage.ObservedValue);
        Assert.Equal(160, damage.AuthoritativeMetric?.Value);
        Assert.Equal(160, Assert.Single(damage.Targets).ObservedValue);
        Assert.Equal(1, damage.CriticalCount);
        Assert.Equal(60, damage.CriticalObservedValue);
        Assert.Equal("Opponent", Assert.Single(damage.Targets).Entity.Name);
        Assert.Equal(1, source.EffectCount);
    }

    [Fact]
    public void Split_health_and_shield_adjustments_count_as_one_critical_damage_effect()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        simulation.Frames.Add(DamageFrame(-885, -639, isCritical: false));
        simulation.Frames.Add(DamageFrame(-2_192, -856, isCritical: true));
        simulation.Frames.Add(DamageFrame(-2_120, -928, isCritical: true));
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 7_620,
            [CombatCardStat.UseCount] = 3,
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var damage = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.DirectDamage
        );

        Assert.Equal(3, source.UseCount);
        Assert.Equal(3, source.EffectCount);
        Assert.Equal(3, damage.Count);
        Assert.Equal(7_620, damage.ObservedValue);
        Assert.Equal(7_620, damage.AuthoritativeMetric?.Value);
        Assert.Equal(2, damage.CriticalCount);
        Assert.Equal(3, damage.CriticalOutcomeCount);
        Assert.Equal(6_096, damage.CriticalObservedValue);
    }

    [Fact]
    public void Recovers_non_damage_crit_from_a_native_on_card_critted_trigger()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var origin = new CombatFrame
        {
            OpponentUpdates = new CombatPlayerUpdate
            {
                Attributes =
                {
                    [CombatPlayerAttribute.Burn] = new CombatPlayerAttributeUpdate
                    {
                        AttributeType = CombatPlayerAttribute.Burn,
                        PreviousValue = 0,
                        CurrentValue = 3,
                    },
                },
            },
        };
        origin.Events.Add(
            Executed(
                "source",
                CombatActionType.PlayerBurnApply,
                Player(Combatant.Opponent),
                triggerSource: "source",
                effectId: "critical-effect"
            )
        );
        origin.Events.Add(
            Executed(
                "source",
                CombatActionType.PlayerShieldApply,
                Player(Combatant.Player),
                triggerSource: "source"
            )
        );
        origin.PlayerUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Shield,
                    AttributeChanged = CombatHealthChange.Shield,
                    Amount = 15,
                    IsCrit = false,
                },
            },
        };
        simulation.Frames.Add(origin);

        var evidence = new CombatFrame();
        var gain = Executed(
            "crit-listener",
            CombatActionType.CardModifyAttribute,
            CardTarget("target"),
            triggerSource: "source"
        );
        gain.EffectId = "crit-gain";
        evidence.Events.Add(gain);
        evidence.CardUpdates[("target")] = new CombatCardUpdate
        {
            CardInstanceId = ("target"),
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 10,
                    CurrentValue = 20,
                },
            },
        };
        simulation.Frames.Add(evidence);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 3,
            [CombatCardStat.ShieldAdded] = 15,
            [CombatCardStat.UseCount] = 1,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.BurnApplyAmount] = 3,
                [CombatCardAttribute.ShieldApplyAmount] = 15,
            },
            CritCapableEffectIds = ["critical-effect"],
        };
        entities["crit-listener"] = new CombatImpactEntity(
            "crit-listener",
            "Boombox",
            "Item",
            null,
            5,
            AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                ["crit-gain"] = CombatCardAttribute.DamageAmount,
            },
            CriticalTriggerAbilitiesByEffectId: new Dictionary<string, CombatEffectPriority>
            {
                ["crit-gain"] = CombatEffectPriority.Low,
            }
        );

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(report.Sources, candidate => candidate.Entity.Id == "source");
        var burn = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Burn);
        var shield = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Shield);

        Assert.Equal(1, burn.CriticalCount);
        Assert.Equal(1, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
        Assert.Equal(0, shield.CriticalCount);
        Assert.Equal(2, source.Groups.Count);
        Assert.Equal(
            report.CriticalTriggerEvidenceAudit.ResolvedOriginCount,
            report.CriticalTriggerEvidenceAudit.AttributedOriginCount
        );
        Assert.Equal(1, report.CriticalTriggerEvidenceAudit.ResolvedOriginCount);
    }

    [Fact]
    public void Does_not_label_on_card_critted_derived_damage_as_critical()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();

        var fired = new CombatFrame
        {
            PlayerUpdates = new CombatPlayerUpdate
            {
                HealthAdjustments =
                {
                    new CombatHealthAdjustment
                    {
                        DamageType = CombatDamageType.Shield,
                        AttributeChanged = CombatHealthChange.Shield,
                        Amount = 210,
                        IsCrit = true,
                    },
                },
            },
        };
        var shield = Executed(
            "hairpins",
            CombatActionType.PlayerShieldApply,
            Player(Combatant.Player),
            triggerSource: "hairpins",
            executionContextId: "hairpins-fired"
        );
        shield.EffectId = "1";
        fired.Events.Add(shield);
        simulation.Frames.Add(fired);

        var critTriggered = new CombatFrame
        {
            OpponentUpdates = new CombatPlayerUpdate
            {
                HealthAdjustments =
                {
                    // Runtime adjustment matching can inherit the originating activation's crit
                    // flag. The on-card-critted action itself must still remain non-critical.
                    DamageAdjustment(CombatHealthChange.Health, -315, isCritical: true),
                },
            },
        };
        var damage = Executed(
            "hairpins",
            CombatActionType.PlayerDamage,
            Player(Combatant.Opponent),
            triggerSource: "hairpins",
            executionContextId: "hairpins-on-crit"
        );
        damage.EffectId = "0";
        critTriggered.Events.Add(damage);
        simulation.Frames.Add(critTriggered);
        simulation.CardStats["hairpins"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 315,
            [CombatCardStat.ShieldAdded] = 210,
            [CombatCardStat.UseCount] = 1,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["hairpins"] = new CombatImpactEntity(
            "hairpins",
            "Hairpins",
            "Item",
            null,
            5,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.DamageAmount] = 315,
                [CombatCardAttribute.ShieldApplyAmount] = 105,
            },
            CriticalTriggerAbilitiesByEffectId: new Dictionary<string, CombatEffectPriority>
            {
                ["0"] = CombatEffectPriority.Medium,
            },
            CritCapableEffectIds: ["1"]
        );

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(report.Sources, candidate => candidate.Entity.Id == "hairpins");
        var damageGroup = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.DirectDamage
        );
        var shieldGroup = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.Shield
        );

        Assert.Equal(0, damageGroup.CriticalCount);
        Assert.Equal(1, shieldGroup.CriticalCount);
        Assert.Equal(1, report.CriticalTriggerEvidenceAudit.ResolvedOriginCount);
        Assert.Equal(1, report.CriticalTriggerEvidenceAudit.AttributedOriginCount);
    }

    [Fact]
    public void Does_not_apply_on_card_critted_evidence_to_an_unrelated_source()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var origin = new CombatFrame
        {
            OpponentUpdates = new CombatPlayerUpdate
            {
                HealthAdjustments =
                {
                    new CombatHealthAdjustment
                    {
                        DamageType = CombatDamageType.Damage,
                        AttributeChanged = CombatHealthChange.Health,
                        Amount = -10,
                    },
                },
            },
        };
        origin.Events.Add(
            Executed(
                "source",
                CombatActionType.PlayerDamage,
                Player(Combatant.Opponent),
                triggerSource: "source"
            )
        );
        simulation.Frames.Add(origin);

        var evidence = new CombatFrame();
        var gain = Executed(
            "crit-listener",
            CombatActionType.CardModifyAttribute,
            CardTarget("target"),
            triggerSource: "trigger"
        );
        gain.EffectId = "crit-gain";
        evidence.Events.Add(gain);
        evidence.CardUpdates[("target")] = DamageUpdate(("target"), 10, 20);
        simulation.Frames.Add(evidence);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 10,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["crit-listener"] = new CombatImpactEntity(
            "crit-listener",
            "Boombox",
            "Item",
            null,
            5,
            AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                ["crit-gain"] = CombatCardAttribute.DamageAmount,
            },
            CriticalTriggerAbilitiesByEffectId: new Dictionary<string, CombatEffectPriority>
            {
                ["crit-gain"] = CombatEffectPriority.Low,
            }
        );

        var damage = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector.Project(simulation, entities).Sources,
                    candidate => candidate.Entity.Id == "source"
                )
                .Groups
        );

        Assert.Equal(0, damage.CriticalCount);
    }

    [Fact]
    public void Deduplicates_native_and_on_card_critted_evidence_for_one_activation()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var origin = new CombatFrame
        {
            OpponentUpdates = new CombatPlayerUpdate
            {
                HealthAdjustments =
                {
                    DamageAdjustment(CombatHealthChange.Health, -20, isCritical: true),
                },
            },
        };
        origin.Events.Add(
            Executed(
                "source",
                CombatActionType.PlayerDamage,
                Player(Combatant.Opponent),
                triggerSource: "source"
            )
        );
        simulation.Frames.Add(origin);

        var listener = new CombatFrame();
        var gain = Executed(
            "crit-listener",
            CombatActionType.CardModifyAttribute,
            CardTarget("target"),
            triggerSource: "source"
        );
        gain.EffectId = "crit-gain";
        listener.Events.Add(gain);
        listener.CardUpdates[("target")] = DamageUpdate(("target"), 10, 20);
        simulation.Frames.Add(listener);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 20,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["crit-listener"] = CriticalTriggerEntity(CombatEffectPriority.Low);

        var damage = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector.Project(simulation, entities).Sources,
                    source => source.Entity.Id == "source"
                )
                .Groups
        );

        Assert.Equal(1, damage.CriticalCount);
        Assert.Equal(1, damage.CriticalOutcomeCount);
    }

    [Fact]
    public void Associates_low_priority_crit_evidence_with_a_same_frame_origin()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var frame = PlayerEffectFrame(
            CombatActionType.PlayerBurnApply,
            CombatPlayerAttribute.Burn,
            0,
            6
        );
        var gain = Executed(
            "crit-listener",
            CombatActionType.CardModifyAttribute,
            CardTarget("target"),
            triggerSource: "source"
        );
        gain.EffectId = "crit-gain";
        frame.Events.Add(gain);
        frame.CardUpdates[("target")] = DamageUpdate(("target"), 10, 20);
        simulation.Frames.Add(frame);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 6,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.BurnApplyAmount] = 3,
            },
            CritCapableEffectIds = ["critical-effect"],
        };
        entities["crit-listener"] = CriticalTriggerEntity(CombatEffectPriority.Low);

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector.Project(simulation, entities).Sources,
                    source => source.Entity.Id == "source"
                )
                .Groups
        );

        Assert.Equal(1, burn.CriticalCount);
    }

    [Fact]
    public void Critical_trigger_attribution_work_scales_linearly_with_evidence()
    {
        var smaller = ProjectCriticalTriggerScalingCase(64).CriticalTriggerEvidenceAudit;
        var larger = ProjectCriticalTriggerScalingCase(128).CriticalTriggerEvidenceAudit;

        Assert.Equal(64, smaller.ObservedEvidenceCount);
        Assert.Equal(128, larger.ObservedEvidenceCount);
        Assert.InRange(smaller.ResolvedOriginCount, 63, 64);
        Assert.InRange(larger.ResolvedOriginCount, 127, 128);
        Assert.Equal(smaller.ResolvedOriginCount, smaller.AttributedOriginCount);
        Assert.Equal(larger.ResolvedOriginCount, larger.AttributedOriginCount);
        Assert.True(smaller.WorkUnitCount > 0);
        Assert.True(
            larger.WorkUnitCount <= smaller.WorkUnitCount * 2 + 16,
            $"Expected linear critical-trigger work, got {smaller.WorkUnitCount} -> {larger.WorkUnitCount}."
        );
    }

    [Fact]
    public void Does_not_associate_delayed_crit_evidence_with_an_unrelated_same_frame_activation()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        simulation.Frames.Add(
            PlayerEffectFrame(CombatActionType.PlayerBurnApply, CombatPlayerAttribute.Burn, 0, 6)
        );
        var evidence = PlayerEffectFrame(
            CombatActionType.PlayerBurnApply,
            CombatPlayerAttribute.Burn,
            6,
            9
        );
        var gain = Executed(
            "crit-listener",
            CombatActionType.CardModifyAttribute,
            CardTarget("target"),
            triggerSource: "source"
        );
        gain.EffectId = "crit-gain";
        evidence.Events.Add(gain);
        evidence.CardUpdates[("target")] = DamageUpdate(("target"), 10, 20);
        simulation.Frames.Add(evidence);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 9,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.BurnApplyAmount] = 3,
            },
            CritCapableEffectIds = ["critical-effect"],
        };
        entities["crit-listener"] = CriticalTriggerEntity(CombatEffectPriority.Low);

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector.Project(simulation, entities).Sources,
                    source => source.Entity.Id == "source"
                )
                .Groups
        );

        Assert.Equal(1, burn.CriticalCount);
        Assert.Equal(2, burn.Count);
    }

    [Fact]
    public void Recovers_damage_crits_when_multiple_same_frame_attacks_hide_native_adjustments()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -37_454,
                    IsCrit = true,
                },
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -86,
                    IsCrit = true,
                },
            },
        };
        var sourceInstance = ("source");
        simulation.Frames[0].CardUpdates[sourceInstance] = new CombatCardUpdate
        {
            CardInstanceId = sourceInstance,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 359,
                    CurrentValue = 18_727,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 37_454,
        };
        simulation.CardStats["trigger"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 86,
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.DamageAmount] = 359,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.DamageAmount] = 43,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        var sourceDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        Assert.Equal(37_454, sourceDamage.ObservedValue);
        Assert.Equal(37_454, sourceDamage.AuthoritativeMetric?.Value);
        Assert.Equal(1, sourceDamage.CriticalCount);
        Assert.Equal(37_454, sourceDamage.CriticalObservedValue);

        var triggerDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );
        Assert.Equal(86, triggerDamage.ObservedValue);
        Assert.Equal(86, triggerDamage.AuthoritativeMetric?.Value);
        Assert.Equal(1, triggerDamage.CriticalCount);
        Assert.Equal(86, triggerDamage.CriticalObservedValue);
    }

    [Fact]
    public void Maps_mixed_concurrent_damage_crits_from_native_adjustment_order_without_stats()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                DamageAdjustment(CombatHealthChange.Health, -32, isCritical: false),
                DamageAdjustment(CombatHealthChange.Health, -62, isCritical: true),
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var sourceDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        var triggerDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );

        Assert.Equal(32, sourceDamage.ObservedValue);
        Assert.Equal(0, sourceDamage.CriticalCount);
        Assert.Equal(62, triggerDamage.ObservedValue);
        Assert.Equal(1, triggerDamage.CriticalCount);
        Assert.Equal(62, triggerDamage.CriticalObservedValue);
    }

    [Fact]
    public void Maps_split_shield_and_health_damage_before_the_next_concurrent_effect()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                DamageAdjustment(CombatHealthChange.Health, -109, isCritical: true),
                DamageAdjustment(CombatHealthChange.Shield, -11, isCritical: true),
                DamageAdjustment(CombatHealthChange.Health, -60, isCritical: false),
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var sourceDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        var triggerDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );

        Assert.Equal(120, sourceDamage.ObservedValue);
        Assert.Equal(1, sourceDamage.CriticalCount);
        Assert.Equal(120, sourceDamage.CriticalObservedValue);
        Assert.Equal(60, triggerDamage.ObservedValue);
        Assert.Equal(0, triggerDamage.CriticalCount);
    }

    [Fact]
    public void Maps_concurrent_heal_and_shield_crits_from_native_adjustments()
    {
        var cases = new[]
        {
            (
                CombatActionType.PlayerHeal,
                CombatDamageType.Heal,
                CombatHealthChange.Health,
                CombatImpactKind.Healing
            ),
            (
                CombatActionType.PlayerShieldApply,
                CombatDamageType.Shield,
                CombatHealthChange.Shield,
                CombatImpactKind.Shield
            ),
        };

        foreach (var (action, damageType, attribute, expectedKind) in cases)
        {
            var simulation = new CombatTimeline();
            simulation.Frames[0].Events.Add(Executed("source", action, Player(Combatant.Player)));
            simulation.Frames[0].Events.Add(Executed("trigger", action, Player(Combatant.Player)));
            simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
            {
                HealthAdjustments =
                {
                    new CombatHealthAdjustment
                    {
                        DamageType = damageType,
                        AttributeChanged = attribute,
                        Amount = 200,
                        IsCrit = true,
                    },
                    new CombatHealthAdjustment
                    {
                        DamageType = damageType,
                        AttributeChanged = attribute,
                        Amount = 100,
                        IsCrit = false,
                    },
                },
            };

            var report = CombatImpactProjector.Project(simulation, Entities());
            var sourceGroup = Assert.Single(
                Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
            );
            var triggerGroup = Assert.Single(
                Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
            );

            Assert.Equal(expectedKind, sourceGroup.Kind);
            Assert.Equal(200, sourceGroup.ObservedValue);
            Assert.Equal(1, sourceGroup.CriticalCount);
            Assert.Equal(200, sourceGroup.CriticalObservedValue);
            Assert.Equal(expectedKind, triggerGroup.Kind);
            Assert.Equal(100, triggerGroup.ObservedValue);
            Assert.Equal(0, triggerGroup.CriticalCount);
        }
    }

    [Fact]
    public void Does_not_infer_damage_crits_without_a_native_critical_adjustment()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -20,
                    IsCrit = false,
                },
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -10,
                    IsCrit = false,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 20,
        };
        simulation.CardStats["trigger"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 10,
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.DamageAmount] = 10,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.DamageAmount] = 5,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.All(
            report.Sources,
            source => Assert.Equal(0, Assert.Single(source.Groups).CriticalCount)
        );
    }

    [Fact]
    public void Uniform_native_crits_mark_each_concurrent_damage_effect_without_stat_inference()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        for (var use = 0; use < 4; use++)
            simulation.Frames.Add(ConcurrentDamageFrame(isCritical: false));
        simulation.Frames.Add(ConcurrentDamageFrame(isCritical: true, splitFirstHit: true));
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            // Deliberately inconsistent with the configured 30-damage baseline. The native
            // per-adjustment crit flags, not an inferred total, are authoritative for this case.
            [CombatCardStat.DamageDone] = 420,
            [CombatCardStat.UseCount] = 5,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.DamageAmount] = 30,
            },
        };

        var damage = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, entities).Sources).Groups
        );

        Assert.Equal(15, damage.Count);
        Assert.Equal(420, damage.AuthoritativeMetric?.Value);
        Assert.Equal(3, damage.CriticalCount);
        Assert.Equal(15, damage.CriticalOutcomeCount);
        Assert.Equal(200, damage.CriticalObservedValue);
    }

    [Fact]
    public void Merges_observed_burn_and_poison_with_authoritative_totals()
    {
        var cases = new[]
        {
            (
                CombatActionType.PlayerBurnApply,
                CombatPlayerAttribute.Burn,
                CombatCardStat.BurnAdded,
                CombatImpactKind.Burn,
                "BurnApplyAmount"
            ),
            (
                CombatActionType.PlayerPoisonApply,
                CombatPlayerAttribute.Poison,
                CombatCardStat.PoisonAdded,
                CombatImpactKind.Poison,
                "PoisonApplyAmount"
            ),
        };
        foreach (var (action, attribute, statistic, expectedKind, canonicalKey) in cases)
        {
            var simulation = new CombatTimeline();
            simulation.Frames[0].Events.Add(Executed("source", action, Player(Combatant.Opponent)));
            simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
            {
                Attributes =
                {
                    [attribute] = new CombatPlayerAttributeUpdate
                    {
                        AttributeType = attribute,
                        PreviousValue = 0,
                        CurrentValue = 69,
                    },
                },
            };
            simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
            {
                [statistic] = 76,
            };

            var source = Assert.Single(
                CombatImpactProjector.Project(simulation, Entities()).Sources
            );
            var group = Assert.Single(source.Groups);

            Assert.Equal(expectedKind, group.Kind);
            Assert.Equal(canonicalKey, group.NativeAttributeKey);
            Assert.Equal(69, group.ObservedValue);
            Assert.Equal(76, group.AuthoritativeMetric?.Value);
            Assert.Equal(69, Assert.Single(group.Targets).ObservedValue);
        }
    }

    [Fact]
    public void Resolves_exact_status_crit_outcomes_from_unique_application_amounts()
    {
        var cases = new[]
        {
            (
                CombatActionType.PlayerBurnApply,
                CombatPlayerAttribute.Burn,
                CombatCardAttribute.BurnApplyAmount,
                CombatCardStat.BurnAdded,
                CombatImpactKind.Burn
            ),
            (
                CombatActionType.PlayerPoisonApply,
                CombatPlayerAttribute.Poison,
                CombatCardAttribute.PoisonApplyAmount,
                CombatCardStat.PoisonAdded,
                CombatImpactKind.Poison
            ),
            (
                CombatActionType.PlayerRegenApply,
                CombatPlayerAttribute.HealthRegen,
                CombatCardAttribute.RegenApplyAmount,
                CombatCardStat.RegenAdded,
                CombatImpactKind.AttributeChange
            ),
        };
        foreach (var (action, playerAttribute, sourceAttribute, statistic, expectedKind) in cases)
        {
            var simulation = new CombatTimeline();
            simulation.Frames.Clear();
            var previous = 0;
            foreach (var appliedValue in new[] { 4, 8 })
            {
                var frame = new CombatFrame();
                var executed = Executed("source", action, Player(Combatant.Player));
                executed.EffectId = "critical-effect";
                frame.Events.Add(executed);
                frame.PlayerUpdates = new CombatPlayerUpdate
                {
                    Attributes =
                    {
                        [playerAttribute] = new CombatPlayerAttributeUpdate
                        {
                            AttributeType = playerAttribute,
                            PreviousValue = previous,
                            CurrentValue = previous + appliedValue,
                        },
                    },
                };
                simulation.Frames.Add(frame);
                previous += appliedValue;
            }
            simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
            {
                [statistic] = 12,
            };

            var group = Assert.Single(
                Assert
                    .Single(
                        CombatImpactProjector
                            .Project(simulation, EntitiesWithSourceAttribute(sourceAttribute, 4))
                            .Sources
                    )
                    .Groups
            );

            Assert.Equal(expectedKind, group.Kind);
            Assert.Equal(2, group.Count);
            Assert.Equal(1, group.CriticalCount);
            Assert.Equal(2, group.CriticalOutcomeCount);
            Assert.Equal(8, group.CriticalObservedValue);
        }
    }

    [Fact]
    public void Does_not_infer_a_crit_for_burn_triggered_by_another_item()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        simulation.Frames.Add(
            PlayerEffectFrame(
                CombatActionType.PlayerBurnApply,
                CombatPlayerAttribute.Burn,
                0,
                4,
                effectId: "passive-burn",
                triggerSource: "trigger"
            )
        );
        simulation.Frames.Add(
            PlayerEffectFrame(
                CombatActionType.PlayerBurnApply,
                CombatPlayerAttribute.Burn,
                4,
                12,
                effectId: "passive-burn",
                triggerSource: "trigger"
            )
        );
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 12,
        };
        var entities = EntitiesWithSourceAttribute(CombatCardAttribute.BurnApplyAmount, 4)
            .ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.BurnApplyAmount] = 4,
                [CombatCardAttribute.CritChance] = 12,
            },
            CritCapableEffectIds = null,
        };

        var burn = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, entities).Sources).Groups
        );

        Assert.Equal(2, burn.Count);
        Assert.Equal(0, burn.CriticalCount);
        Assert.Equal(0, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
        Assert.Equal(CombatImpactTriggerPresentationState.Complete, burn.TriggerPresentationState);
    }

    [Fact]
    public void Resolves_only_dynamic_burn_crits_supported_by_each_application()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 4, 5));
        AddPlayerEffectFrames(simulation, CombatPlayerAttribute.Burn, 0, 10, 14);
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 5, 6));
        AddPlayerEffectFrames(simulation, CombatPlayerAttribute.Burn, 14, 25);
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 6, 7));
        AddPlayerEffectFrames(simulation, CombatPlayerAttribute.Burn, 25, 39, 53);
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 7, 8));
        AddPlayerEffectFrames(simulation, CombatPlayerAttribute.Burn, 53, 69);
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 8, 9));
        AddPlayerEffectFrames(simulation, CombatPlayerAttribute.Burn, 69, 87);
        var sameFrameGain = PlayerEffectFrame(
            CombatActionType.PlayerBurnApply,
            CombatPlayerAttribute.Burn,
            87,
            105
        );
        SetSourceAttributeUpdate(sameFrameGain, CombatCardAttribute.BurnApplyAmount, 9, 10);
        simulation.Frames.Add(sameFrameGain);
        AddPlayerEffectFrames(simulation, CombatPlayerAttribute.Burn, 105, 125, 145);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 147,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(CombatCardAttribute.BurnApplyAmount, 4)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(10, burn.Count);
        Assert.Equal(145, burn.ObservedValue);
        Assert.Equal(147, burn.AuthoritativeMetric?.Value);
        Assert.Equal(8, burn.CriticalCount);
        Assert.Equal(8, burn.CriticalOutcomeCount);
        Assert.Equal(130, burn.CriticalObservedValue);
    }

    [Fact]
    public void Keeps_same_frame_status_amount_change_ambiguous()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var frame = PlayerEffectFrame(
            CombatActionType.PlayerBurnApply,
            CombatPlayerAttribute.Burn,
            0,
            8
        );
        SetSourceAttributeUpdate(frame, CombatCardAttribute.BurnApplyAmount, 4, 8);
        simulation.Frames.Add(frame);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 8,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(CombatCardAttribute.BurnApplyAmount, 4)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(1, burn.Count);
        Assert.Equal(8, burn.ObservedValue);
        Assert.Equal(0, burn.CriticalCount);
        Assert.Equal(0, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
    }

    [Fact]
    public void Carries_native_activation_crits_from_damage_to_burn_without_inferring_magnitude()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var previousBurn = 0;
        var damageValues = new[] { 1_016, 528, 2_152 };
        var burnValues = new[] { 102, 53, 216 };
        var critical = new[] { true, false, true };
        for (var index = 0; index < damageValues.Length; index++)
        {
            var frame = new CombatFrame
            {
                OpponentUpdates = new CombatPlayerUpdate
                {
                    Attributes =
                    {
                        [CombatPlayerAttribute.Burn] = new CombatPlayerAttributeUpdate
                        {
                            AttributeType = CombatPlayerAttribute.Burn,
                            PreviousValue = previousBurn,
                            CurrentValue = previousBurn + burnValues[index],
                        },
                    },
                    HealthAdjustments =
                    {
                        DamageAdjustment(
                            CombatHealthChange.Health,
                            -damageValues[index],
                            critical[index]
                        ),
                    },
                },
            };
            frame.Events.Add(
                Executed(
                    "source",
                    CombatActionType.PlayerDamage,
                    Player(Combatant.Opponent),
                    triggerSource: "source",
                    effectId: "critical-effect"
                )
            );
            frame.Events.Add(
                Executed(
                    "source",
                    CombatActionType.PlayerBurnApply,
                    Player(Combatant.Opponent),
                    triggerSource: "source",
                    effectId: "critical-effect"
                )
            );
            simulation.Frames.Add(frame);
            previousBurn += burnValues[index];
        }
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = damageValues.Sum(),
            [CombatCardStat.BurnAdded] = burnValues.Sum(),
            [CombatCardStat.UseCount] = 3,
        };

        var source = Assert.Single(
            CombatImpactProjector
                .Project(
                    simulation,
                    EntitiesWithSourceAttributes(
                        (CombatCardAttribute.DamageAmount, 254),
                        (CombatCardAttribute.BurnApplyAmount, 51)
                    )
                )
                .Sources
        );
        var damage = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.DirectDamage
        );
        var burn = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Burn);

        Assert.Equal(2, damage.CriticalCount);
        Assert.Equal(3, damage.CriticalOutcomeCount);
        Assert.Equal(3_168, damage.CriticalObservedValue);
        Assert.Equal(2, burn.CriticalCount);
        Assert.Equal(3, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
        Assert.Equal(371, burn.AuthoritativeMetric?.Value);
    }

    [Fact]
    public void Does_not_treat_same_frame_noncritical_damage_as_burn_critical_evidence()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var previousBurn = 0;
        foreach (var burnValue in new[] { 4, 8 })
        {
            var frame = new CombatFrame
            {
                OpponentUpdates = new CombatPlayerUpdate
                {
                    Attributes =
                    {
                        [CombatPlayerAttribute.Burn] = new CombatPlayerAttributeUpdate
                        {
                            AttributeType = CombatPlayerAttribute.Burn,
                            PreviousValue = previousBurn,
                            CurrentValue = previousBurn + burnValue,
                        },
                    },
                    HealthAdjustments =
                    {
                        DamageAdjustment(CombatHealthChange.Health, -20, isCritical: false),
                    },
                },
            };
            frame.Events.Add(
                Executed(
                    "source",
                    CombatActionType.PlayerDamage,
                    Player(Combatant.Opponent),
                    triggerSource: "source",
                    effectId: "critical-effect"
                )
            );
            frame.Events.Add(
                Executed(
                    "source",
                    CombatActionType.PlayerBurnApply,
                    Player(Combatant.Opponent),
                    triggerSource: "source",
                    effectId: "critical-effect"
                )
            );
            simulation.Frames.Add(frame);
            previousBurn += burnValue;
        }
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 40,
            [CombatCardStat.BurnAdded] = 12,
            [CombatCardStat.UseCount] = 2,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttributes(
                                (CombatCardAttribute.DamageAmount, 10),
                                (CombatCardAttribute.BurnApplyAmount, 4)
                            )
                        )
                        .Sources
                )
                .Groups,
            group => group.Kind == CombatImpactKind.Burn
        );

        Assert.Equal(2, burn.Count);
        Assert.Equal(0, burn.CriticalCount);
        Assert.Equal(2, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
    }

    [Fact]
    public void Does_not_carry_criticality_between_different_same_frame_activations()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.PlayerDamage,
                    Player(Combatant.Opponent),
                    triggerSource: "damage-trigger",
                    effectId: "critical-effect"
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.PlayerBurnApply,
                    Player(Combatant.Opponent),
                    triggerSource: "burn-trigger",
                    effectId: "critical-effect"
                )
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Burn] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Burn,
                    PreviousValue = 0,
                    CurrentValue = 8,
                },
            },
            HealthAdjustments =
            {
                DamageAdjustment(CombatHealthChange.Health, -20, isCritical: true),
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 20,
            [CombatCardStat.BurnAdded] = 8,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(CombatCardAttribute.DamageAmount, 10)
                        )
                        .Sources
                )
                .Groups,
            group => group.Kind == CombatImpactKind.Burn
        );

        Assert.Equal(0, burn.CriticalCount);
        Assert.Equal(0, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
    }

    [Fact]
    public void Does_not_guess_crit_count_when_dynamic_apply_values_have_ambiguous_subsets()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        simulation.Frames.Add(
            PlayerEffectFrame(CombatActionType.PlayerBurnApply, CombatPlayerAttribute.Burn, 0, 1)
        );
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 1, 2));
        simulation.Frames.Add(
            PlayerEffectFrame(CombatActionType.PlayerBurnApply, CombatPlayerAttribute.Burn, 1, 3)
        );
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.BurnApplyAmount, 2, 3));
        simulation.Frames.Add(
            PlayerEffectFrame(CombatActionType.PlayerBurnApply, CombatPlayerAttribute.Burn, 3, 6)
        );
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = 9,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(CombatCardAttribute.BurnApplyAmount, 1)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(0, burn.CriticalCount);
        Assert.Equal(3, burn.CriticalOutcomeCount);
        Assert.Null(burn.CriticalObservedValue);
    }

    [Fact]
    public void Generic_player_attribute_changes_use_the_unique_same_frame_transition()
    {
        var cases = new List<(
            CombatPlayerAttribute Attribute,
            CombatCardStat Statistic,
            string CanonicalKey
        )>
        {
            (CombatPlayerAttribute.HealthRegen, CombatCardStat.RegenAdded, "RegenApplyAmount"),
            (CombatPlayerAttribute.Rage, CombatCardStat.RageAdded, "RageApplyAmount"),
            (CombatPlayerAttribute.Tempo, CombatCardStat.TempoAdded, "TempoApplyAmount"),
        };
        foreach (var (attribute, statistic, canonicalKey) in cases)
        {
            var simulation = new CombatTimeline();
            simulation
                .Frames[0]
                .Events.Add(
                    Executed(
                        "source",
                        CombatActionType.PlayerModifyAttribute,
                        Player(Combatant.Player)
                    )
                );
            simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
            {
                Attributes =
                {
                    [attribute] = new CombatPlayerAttributeUpdate
                    {
                        AttributeType = attribute,
                        PreviousValue = 0,
                        CurrentValue = 12,
                    },
                },
            };
            simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
            {
                [statistic] = 20,
            };

            var source = Assert.Single(
                CombatImpactProjector.Project(simulation, Entities()).Sources
            );
            var concrete = Assert.Single(source.Groups, group => group.Count == 1);
            var authoritative = Assert.Single(
                source.Groups,
                group => group.AuthoritativeMetric != null
            );
            Assert.Equal(CombatImpactKind.AttributeChange, concrete.Kind);
            Assert.Equal(CombatImpactEventSurface.PlayerAttribute, concrete.Surface);
            Assert.Equal(attribute.ToString(), concrete.NativeAttributeKey);
            Assert.Equal(12, concrete.ObservedValue);
            Assert.Equal(CombatImpactValueUnit.Amount, concrete.Unit);
            Assert.Equal(CombatImpactCoverage.LowerBound, concrete.ObservedCoverage);
            Assert.Equal(canonicalKey, authoritative.NativeAttributeKey);
            Assert.Equal(CombatImpactEventSurface.AppliedEffect, authoritative.Surface);
            Assert.Equal(20, authoritative.AuthoritativeMetric?.Value);
        }
    }

    [Fact]
    public void Card_stats_map_to_explicit_authoritative_metric_domains()
    {
        var simulation = new CombatTimeline();
        var statistics = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 1,
            [CombatCardStat.ShieldAdded] = 2,
            [CombatCardStat.HealAdded] = 3,
            [CombatCardStat.PoisonAdded] = 4,
            [CombatCardStat.BurnAdded] = 5,
            [CombatCardStat.HastedCardsCount] = 6,
            [CombatCardStat.SlowedCardsCount] = 7,
            [CombatCardStat.FrozenCardsCount] = 8,
            [CombatCardStat.RegenAdded] = 9,
            [CombatCardStat.RageAdded] = 10,
            [CombatCardStat.UseCount] = 13,
            [CombatCardStat.TempoAdded] = 11,
            [CombatCardStat.TempoSpent] = 12,
        };
        simulation.CardStats["source"] = statistics;

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var metrics = source
            .Groups.Select(group => group.AuthoritativeMetric)
            .Where(metric => metric != null)
            .Cast<CombatImpactAuthoritativeMetric>()
            .ToArray();

        Assert.Equal(13, source.UseCount);
        Assert.Equal(12, metrics.Length);
        Assert.Equal(
            9,
            metrics.Count(metric =>
                metric.Basis == CombatImpactAuthoritativeBasis.TotalAmount
                && metric.Unit == CombatImpactValueUnit.Amount
            )
        );
        Assert.Equal(
            3,
            metrics.Count(metric =>
                metric.Basis == CombatImpactAuthoritativeBasis.ApplicationCount
                && metric.Unit == CombatImpactValueUnit.Applications
            )
        );
        Assert.Contains(
            metrics,
            metric =>
                metric.NativeAttributeKey == "TempoApplyAmount"
                && metric.Value == 11
                && metric.Basis == CombatImpactAuthoritativeBasis.TotalAmount
        );
        Assert.Contains(
            metrics,
            metric =>
                metric.NativeAttributeKey == "TempoRemoveAmount"
                && metric.Value == 12
                && metric.Basis == CombatImpactAuthoritativeBasis.TotalAmount
        );
    }

    [Fact]
    public void Card_action_cost_event_is_projected_as_a_tempo_spend()
    {
        var simulation = new CombatTimeline();
        simulation.Frames[0].Events.Add(TempoCostSpent("source"));

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal("TempoRemoveAmount", group.NativeAttributeKey);
        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Null(group.AuthoritativeMetric);
        Assert.Equal(
            CombatImpactProjector.PlayerId(Combatant.Player),
            Assert.Single(group.Targets).Entity.Id
        );
    }

    [Fact]
    public void Projects_tempo_gain_and_card_action_cost_spend_with_authoritative_totals()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var gainFrame = new CombatFrame();
        gainFrame.Events.Add(
            Executed("source", CombatActionType.PlayerTempoApply, Player(Combatant.Player))
        );
        gainFrame.PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Tempo] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Tempo,
                    PreviousValue = 0,
                    CurrentValue = 6,
                },
            },
        };
        simulation.Frames.Add(gainFrame);
        var spendFrame = new CombatFrame();
        spendFrame.Events.Add(TempoCostSpent("source"));
        simulation.Frames.Add(spendFrame);
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.TempoAdded] = 6,
            [CombatCardStat.TempoSpent] = 4,
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(2, source.EffectCount);
        var gained = Assert.Single(
            source.Groups,
            group => group.NativeAttributeKey == "TempoApplyAmount"
        );
        Assert.Equal(1, gained.Count);
        Assert.Equal(6, gained.ObservedValue);
        Assert.Equal(6, gained.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactProjector.PlayerId(Combatant.Player),
            Assert.Single(gained.Targets).Entity.Id
        );

        var spent = Assert.Single(
            source.Groups,
            group => group.NativeAttributeKey == "TempoRemoveAmount"
        );
        Assert.Equal(1, spent.Count);
        Assert.Null(spent.ObservedValue);
        Assert.Equal(4, spent.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactProjector.PlayerId(Combatant.Player),
            Assert.Single(spent.Targets).Entity.Id
        );
    }

    [Fact]
    public void Tempo_gain_does_not_claim_a_same_frame_delta_that_also_includes_cost_spend()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerTempoApply, Player(Combatant.Player))
            );
        simulation.Frames[0].Events.Add(TempoCostSpent("source"));
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Tempo] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Tempo,
                    PreviousValue = 5,
                    CurrentValue = 7,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.TempoAdded] = 6,
            [CombatCardStat.TempoSpent] = 4,
        };

        var groups = Assert
            .Single(CombatImpactProjector.Project(simulation, Entities()).Sources)
            .Groups;

        var gained = Assert.Single(groups, group => group.NativeAttributeKey == "TempoApplyAmount");
        Assert.Null(gained.ObservedValue);
        Assert.Equal(6, gained.AuthoritativeMetric?.Value);
        var spent = Assert.Single(groups, group => group.NativeAttributeKey == "TempoRemoveAmount");
        Assert.Null(spent.ObservedValue);
        Assert.Equal(4, spent.AuthoritativeMetric?.Value);
    }

    [Fact]
    public void Explicit_tempo_remove_reports_the_positive_amount_spent()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerTempoRemove, Player(Combatant.Player))
            );
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Tempo] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Tempo,
                    PreviousValue = 7,
                    CurrentValue = 3,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.TempoSpent] = 4,
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal("TempoRemoveAmount", group.NativeAttributeKey);
        Assert.Equal(4, group.ObservedValue);
        Assert.Equal(4, group.AuthoritativeMetric?.Value);
        Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
    }

    [Fact]
    public void Ambiguous_same_frame_values_are_not_assigned_to_targets()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -200,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.DamageDone] = 200,
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(2, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
        Assert.Equal(200, group.AuthoritativeMetric?.Value);
        var target = Assert.Single(group.Targets);
        Assert.Null(target.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, target.ObservedCoverage);
    }

    [Fact]
    public void Generic_card_modifier_ignores_live_cooldown_when_resolving_damage_amount()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.Cooldown] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.Cooldown,
                    PreviousValue = 5000,
                    CurrentValue = 4950,
                },
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("DamageAmount", group.NativeAttributeKey);
        Assert.Equal(187, group.ObservedValue);
        Assert.Equal(CombatImpactOccurrenceBasis.ExplicitExecution, group.OccurrenceBasis);
    }

    [Fact]
    public void Bass_same_frame_damage_gains_are_split_when_configured_values_match_the_native_delta()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var bassId = ("bass");
        var damage = 1000;

        for (var index = 0; index < 12; index++)
        {
            var frame = new CombatFrame();
            frame.Events.Add(AttributeExecution("bass", "bass-gain", "bass"));
            frame.Events.Add(AttributeExecution("slow-and-steady", "steady-gain", "bass"));
            var delta = 104;
            if (index == 0)
            {
                frame.Events.Add(AttributeExecution("snowstorm", "snowstorm-gain", "bass"));
                delta += 10;
            }
            frame.CardUpdates[bassId] = DamageUpdate(bassId, damage, damage + delta);
            damage += delta;
            simulation.Frames.Add(frame);
        }

        for (var index = 0; index < 2; index++)
        {
            var frame = new CombatFrame();
            frame.Events.Add(AttributeExecution("snowstorm", "snowstorm-gain", "bass"));
            frame.CardUpdates[bassId] = DamageUpdate(bassId, damage, damage + 10);
            damage += 10;
            simulation.Frames.Add(frame);
        }

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["bass"] = AttributeGainEntity("bass", "Bass", 100, "bass-gain", 5);
        entities["slow-and-steady"] = AttributeGainEntity(
            "slow-and-steady",
            "Slow and Steady",
            4,
            "steady-gain",
            6
        );
        entities["snowstorm"] = AttributeGainEntity(
            "snowstorm",
            "Snowstorm",
            10,
            "snowstorm-gain",
            7
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        AssertDamageGain(report, "bass", expectedCount: 12, expectedValue: 1200);
        AssertDamageGain(report, "slow-and-steady", expectedCount: 12, expectedValue: 48);
        AssertDamageGain(report, "snowstorm", expectedCount: 3, expectedValue: 30);

        var received = Assert.Single(report.Received, candidate => candidate.Entity.Id == "bass");
        var incoming = Assert.Single(received.Groups);
        Assert.Equal("DamageAmount", incoming.NativeAttributeKey);
        Assert.Equal(27, incoming.Count);
        Assert.Equal(1278, incoming.ObservedValue);
        Assert.Equal(3, incoming.Sources.Count);
    }

    [Fact]
    public void Concurrent_configured_attribute_mismatch_preserves_counts_and_one_received_residual()
    {
        var simulation = new CombatTimeline();
        var bassId = ("bass");
        simulation.Frames[0].Events.Add(AttributeExecution("bass", "bass-gain", "bass"));
        simulation
            .Frames[0]
            .Events.Add(AttributeExecution("slow-and-steady", "steady-gain", "bass"));
        simulation.Frames[0].CardUpdates[bassId] = DamageUpdate(bassId, 1000, 1105);

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["bass"] = AttributeGainEntity("bass", "Bass", 100, "bass-gain", 5);
        entities["slow-and-steady"] = AttributeGainEntity(
            "slow-and-steady",
            "Slow and Steady",
            4,
            "steady-gain",
            6
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.Equal(2, report.Sources.Count);
        foreach (var sourceId in new[] { "bass", "slow-and-steady" })
        {
            var source = Assert.Single(
                report.Sources,
                candidate => candidate.Entity.Id == sourceId
            );
            var group = Assert.Single(source.Groups);
            Assert.Equal(1, group.Count);
            Assert.Null(group.ObservedValue);
            Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
            Assert.True(group.HasUnattributedTransitionValue);
            Assert.Equal("×1", CombatImpactMetricFormatter.Group(group, chinese: false));

            var target = Assert.Single(group.Targets);
            Assert.Null(target.ObservedValue);
            Assert.True(target.HasUnattributedTransitionValue);
            Assert.Equal("×1", CombatImpactMetricFormatter.Target(group, target, chinese: false));
        }

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal("bass", received.Entity.Id);
        Assert.Equal(2, incoming.Count);
        Assert.Null(incoming.ObservedValue);
        var ledger = Assert.IsType<CombatImpactIncomingTransitionLedger>(incoming.TransitionLedger);
        Assert.Equal(105, ledger.NetValue);
        Assert.Equal(0, ledger.AttributedValue);
        Assert.Equal(105, ledger.ResidualValue);
        Assert.Equal(2, ledger.ResidualApplicationCount);
        Assert.Equal(1, ledger.ResidualFrameCount);
        Assert.Equal(
            "×2 · 105 total",
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false)
        );
        Assert.Equal(
            "×2 · 总计 105",
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: true)
        );
        Assert.All(
            incoming.Sources,
            source =>
                Assert.Equal(
                    "×1",
                    CombatImpactMetricFormatter.IncomingSource(incoming, source, chinese: false)
                )
        );
    }

    [Fact]
    public void Solves_one_statically_unknown_concurrent_attribute_claimant_from_the_net_delta()
    {
        var simulation = new CombatTimeline();
        var targetId = ("target");
        simulation.Frames[0].Events.Add(AttributeExecution("known", "known-gain", "target"));
        simulation.Frames[0].Events.Add(AttributeExecution("unknown", "unknown-gain", "target"));
        simulation.Frames[0].CardUpdates[targetId] = DamageUpdate(targetId, 100, 117);

        var knownModifier = new CombatAttributeModifier
        {
            AttributeType = CombatCardAttribute.DamageAmount,
            Operation = CombatAttributeOperation.Add,
            Value = new CombatFixedValue { Value = 5 },
        };
        var unknownModifier = new CombatAttributeModifier
        {
            AttributeType = CombatCardAttribute.DamageAmount,
            Operation = CombatAttributeOperation.Add,
            Value = new CombatOpaqueReference
            {
                Kind = CombatOpaqueReferenceKind.CardAttributeUnscaled,
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["known"] = AttributeModifierEntity(
            "known",
            "Known Gain",
            "known-gain",
            knownModifier,
            5
        );
        entities["unknown"] = AttributeModifierEntity(
            "unknown",
            "Unknown Gain",
            "unknown-gain",
            unknownModifier,
            6
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        AssertAttributeGain(report, "known", "target", expectedCount: 1, expectedValue: 5);
        AssertAttributeGain(report, "unknown", "target", expectedCount: 1, expectedValue: 12);
        var incoming = Assert.Single(Assert.Single(report.Received).Groups);
        Assert.Equal(17, incoming.ObservedValue);
        Assert.Null(incoming.TransitionLedger);
        var diagnostic = Assert.Single(report.AttributeTransitionDiagnostics);
        Assert.Equal(
            CombatImpactAttributeTransitionResolution.ConcurrentSingleUnknownSolved,
            diagnostic.Resolution
        );
        Assert.Equal(17, diagnostic.AttributedValue);
        Assert.Equal(0, diagnostic.ResidualValue);
        Assert.Equal(0, diagnostic.UnresolvedClaimantCount);
        Assert.Equal(
            [CombatImpactAttributeTransitionFailureReason.CardAttributeUnscaled],
            diagnostic.FailureReasons
        );
    }

    [Fact]
    public void Rejects_a_solved_range_delta_outside_the_configured_bounds()
    {
        var simulation = new CombatTimeline();
        var targetId = ("target");
        simulation.Frames[0].Events.Add(AttributeExecution("known", "known-gain", "target"));
        simulation.Frames[0].Events.Add(AttributeExecution("unknown", "unknown-gain", "target"));
        simulation.Frames[0].CardUpdates[targetId] = DamageUpdate(targetId, 100, 117);

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["known"] = AttributeModifierEntity(
            "known",
            "Known Gain",
            "known-gain",
            new CombatAttributeModifier
            {
                AttributeType = CombatCardAttribute.DamageAmount,
                Operation = CombatAttributeOperation.Add,
                Value = new CombatFixedValue { Value = 5 },
            },
            5
        );
        entities["unknown"] = AttributeModifierEntity(
            "unknown",
            "Range Gain",
            "unknown-gain",
            new CombatAttributeModifier
            {
                AttributeType = CombatCardAttribute.DamageAmount,
                Operation = CombatAttributeOperation.Add,
                Value = new CombatRangeValue { MinValue = 1, MaxValue = 3 },
            },
            6
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.All(
            report.Sources,
            source => Assert.Null(Assert.Single(source.Groups).ObservedValue)
        );
        var incoming = Assert.Single(Assert.Single(report.Received).Groups);
        Assert.Equal(17, incoming.TransitionLedger?.NetValue);
        var diagnostic = Assert.Single(report.AttributeTransitionDiagnostics);
        Assert.Equal(
            CombatImpactAttributeTransitionResolution.ConcurrentResidual,
            diagnostic.Resolution
        );
        Assert.Equal(1, diagnostic.UnresolvedClaimantCount);
        Assert.Equal(
            [CombatImpactAttributeTransitionFailureReason.RangeValue],
            diagnostic.FailureReasons
        );
    }

    [Fact]
    public void Mixed_exact_and_residual_attribute_frames_hide_partial_source_amounts()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var bassId = ("bass");

        var exact = new CombatFrame();
        exact.Events.Add(AttributeExecution("bass", "bass-gain", "bass"));
        exact.Events.Add(AttributeExecution("slow-and-steady", "steady-gain", "bass"));
        exact.CardUpdates[bassId] = DamageUpdate(bassId, 1000, 1104);
        simulation.Frames.Add(exact);

        var residual = new CombatFrame();
        residual.Events.Add(AttributeExecution("bass", "bass-gain", "bass"));
        residual.Events.Add(AttributeExecution("slow-and-steady", "steady-gain", "bass"));
        residual.CardUpdates[bassId] = DamageUpdate(bassId, 1104, 1209);
        simulation.Frames.Add(residual);

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["bass"] = AttributeGainEntity("bass", "Bass", 100, "bass-gain", 5);
        entities["slow-and-steady"] = AttributeGainEntity(
            "slow-and-steady",
            "Slow and Steady",
            4,
            "steady-gain",
            6
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        var bass = Assert.Single(report.Sources, source => source.Entity.Id == "bass");
        var bassGain = Assert.Single(bass.Groups);
        Assert.Equal(100, bassGain.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Partial, bassGain.ObservedCoverage);
        Assert.True(bassGain.HasUnattributedTransitionValue);
        Assert.Equal("×2", CombatImpactMetricFormatter.Group(bassGain, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.Target(
                bassGain,
                Assert.Single(bassGain.Targets),
                chinese: false
            )
        );

        var steady = Assert.Single(report.Sources, source => source.Entity.Id == "slow-and-steady");
        var steadyGain = Assert.Single(steady.Groups);
        Assert.Equal(4, steadyGain.ObservedValue);
        Assert.Equal("×2", CombatImpactMetricFormatter.Group(steadyGain, chinese: false));

        var incoming = Assert.Single(Assert.Single(report.Received).Groups);
        Assert.Equal(4, incoming.Count);
        Assert.Equal(104, incoming.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Partial, incoming.ObservedCoverage);
        var ledger = Assert.IsType<CombatImpactIncomingTransitionLedger>(incoming.TransitionLedger);
        Assert.Equal(209, ledger.NetValue);
        Assert.Equal(104, ledger.AttributedValue);
        Assert.Equal(105, ledger.ResidualValue);
        Assert.Equal(2, ledger.ResidualApplicationCount);
        Assert.Equal(1, ledger.ResidualFrameCount);
        Assert.Equal(
            "×4 · 209 total",
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false)
        );
        Assert.All(
            incoming.Sources,
            source =>
            {
                Assert.True(source.HasUnattributedTransitionValue);
                Assert.Equal(
                    "×2",
                    CombatImpactMetricFormatter.IncomingSource(incoming, source, chinese: false)
                );
            }
        );
    }

    [Fact]
    public void Opposing_residual_attribute_frames_conserve_a_zero_net_transition()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var bassId = ("bass");
        var values = new[] { (Previous: 1000, Current: 1105), (Previous: 1105, Current: 1000) };
        foreach (var (previous, current) in values)
        {
            var frame = new CombatFrame();
            frame.Events.Add(AttributeExecution("bass", "bass-gain", "bass"));
            frame.Events.Add(AttributeExecution("slow-and-steady", "steady-gain", "bass"));
            frame.CardUpdates[bassId] = DamageUpdate(bassId, previous, current);
            simulation.Frames.Add(frame);
        }

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["bass"] = AttributeGainEntity("bass", "Bass", 100, "bass-gain", 5);
        entities["slow-and-steady"] = AttributeGainEntity(
            "slow-and-steady",
            "Slow and Steady",
            4,
            "steady-gain",
            6
        );

        var incoming = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, entities).Received).Groups
        );
        var ledger = Assert.IsType<CombatImpactIncomingTransitionLedger>(incoming.TransitionLedger);

        Assert.Equal(4, incoming.Count);
        Assert.True(incoming.HasMixedValueDirections);
        Assert.Equal(0, ledger.NetValue);
        Assert.Equal(0, ledger.AttributedValue);
        Assert.Equal(0, ledger.ResidualValue);
        Assert.Equal(4, ledger.ResidualApplicationCount);
        Assert.Equal(2, ledger.ResidualFrameCount);
        Assert.Equal(
            "×4 · 0 total",
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false)
        );
    }

    [Fact]
    public void Known_effect_attribute_does_not_fall_back_to_an_unrelated_transition()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        var executed = Executed(
            "source",
            CombatActionType.CardModifyAttribute,
            CardTarget("target")
        );
        executed.EffectId = "0";
        simulation.Frames[0].Events.Add(executed);
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 40,
                    CurrentValue = 50,
                },
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            AbilityAttributeTypesByEffectId = new Dictionary<string, CombatCardAttribute>
            {
                ["0"] = CombatCardAttribute.SellPrice,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.Empty(report.Sources);
    }

    [Fact]
    public void Effect_ids_preserve_concurrent_value_and_crit_gains_from_the_same_replay_frames()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        for (var frameIndex = 0; frameIndex < 3; frameIndex++)
            simulation.Frames.Add(ValueAndCritGainFrame(frameIndex));

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["pen"] = new CombatImpactEntity(
            "pen",
            "PenFT",
            "Item",
            null,
            5,
            AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                ["0"] = CombatCardAttribute.SellPrice,
            }
        );
        entities["caviar"] = new CombatImpactEntity(
            "caviar",
            "Caviar",
            "Item",
            null,
            6,
            AuraAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                ["1"] = CombatCardAttribute.CritChance,
            }
        );
        entities["soul"] = new CombatImpactEntity("soul", "Soul of the District", "Item", null, 7);
        entities["crit-target"] = new CombatImpactEntity(
            "crit-target",
            "Crit Target",
            "Item",
            null,
            8
        );
        entities["value-target"] = new CombatImpactEntity(
            "value-target",
            "Value Target",
            "Item",
            null,
            9
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        var value = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "pen").Groups
        );
        Assert.Equal("SellPrice", value.NativeAttributeKey);
        Assert.Equal(9, value.Count);
        Assert.Equal(27, value.ObservedValue);

        var crit = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "caviar").Groups
        );
        Assert.Equal("CritChance", crit.NativeAttributeKey);
        Assert.Equal(6, crit.Count);
        Assert.Equal(36, crit.ObservedValue);

        var soul = Assert.Single(report.Received, received => received.Entity.Id == "soul");
        Assert.Equal(
            9,
            Assert
                .Single(soul.Groups, group => group.NativeAttributeKey == "SellPrice")
                .ObservedValue
        );
        Assert.Equal(
            18,
            Assert
                .Single(soul.Groups, group => group.NativeAttributeKey == "CritChance")
                .ObservedValue
        );
    }

    [Theory]
    [InlineData(CombatActionType.FlyingStart)]
    [InlineData(CombatActionType.FlyingStop)]
    public void Flying_actions_are_presented_with_exact_source_and_target(CombatActionType action)
    {
        var simulation = new CombatTimeline();
        simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));

        var report = CombatImpactProjector.Project(simulation, Entities());
        var source = Assert.Single(report.Sources);
        var group = Assert.Single(source.Groups);

        Assert.Equal(CombatImpactKind.Flying, group.Kind);
        Assert.Equal("Flying", group.NativeAttributeKey);
        Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
    }

    [Fact]
    public void Keeps_configured_control_duration_separate_from_native_target_count()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    new CombatCardTarget { Target = target }
                )
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.Slow] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.Slow,
                    PreviousValue = 0,
                    CurrentValue = 1950,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.SlowedCardsCount] = 1,
        };

        var group = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(CombatCardAttribute.SlowAmount, 2_000)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(CombatImpactKind.Slow, group.Kind);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, group.Unit);
        Assert.Equal(2_000, group.ObservedValue);
        Assert.Equal(1, group.AuthoritativeMetric?.Value);
        Assert.Equal(CombatImpactValueUnit.Applications, group.AuthoritativeMetric?.Unit);
    }

    [Fact]
    public void Merges_disable_and_destroy_occurrences_in_both_perspectives()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardDisable,
                    new CombatCardTarget { Target = ("target") }
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardDestroy,
                    new CombatCardTarget { Target = ("target") }
                )
            );

        var report = CombatImpactProjector.Project(simulation, Entities());
        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(CombatImpactKind.Destroy, caused.Kind);
        Assert.Equal(
            CombatImpactAggregator.NativeKey(CombatImpactKind.Destroy),
            caused.NativeAttributeKey
        );
        Assert.Equal(2, caused.Count);
        var causedTarget = Assert.Single(caused.Targets);
        Assert.Equal(2, causedTarget.Count);
        Assert.Null(caused.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, caused.ObservedCoverage);
        Assert.Null(caused.AuthoritativeMetric);
        Assert.Equal("×2", CombatImpactMetricFormatter.Group(caused, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.Target(caused, causedTarget, chinese: false)
        );

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Destroy, incoming.Kind);
        Assert.Equal(2, incoming.Count);
        Assert.Equal(2, Assert.Single(incoming.Sources).Count);
        Assert.Null(incoming.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, incoming.ObservedCoverage);
        Assert.Equal("×2", CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.IncomingSource(
                incoming,
                Assert.Single(incoming.Sources),
                chinese: false
            )
        );
    }

    [Theory]
    [InlineData("effect", "source", "source")]
    [InlineData("source", "trigger", "source")]
    [InlineData("effect", "missing", null)]
    public void Source_attribution_prefers_an_activity_direct_source_then_trigger_source(
        string directSource,
        string triggerSource,
        string? expectedSource
    )
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    directSource,
                    CombatActionType.PlayerDamage,
                    Player(Combatant.Opponent),
                    triggerSource
                )
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        var sources = CombatImpactProjector.Project(simulation, Entities()).Sources;
        if (expectedSource == null)
        {
            Assert.Empty(sources);
            return;
        }

        var source = Assert.Single(sources);
        Assert.Equal(expectedSource, source.Entity.Id);
        Assert.Equal(40, Assert.Single(source.Groups).ObservedValue);
    }

    [Fact]
    public void Trigger_sources_count_only_executed_effects()
    {
        // The timeline carries no condition observations: the host adapters drop the game's
        // triggered-without-executed events (CombatImpactAdapter.Tests pins that).
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardFreeze,
                    CardTarget("target"),
                    triggerSource: "trigger",
                    executionContextId: "executed-context"
                )
            );
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = 17,
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(1, source.EffectCount);
        Assert.Equal(17, source.UseCount);
        Assert.Equal(1, source.ObservedActivationBatchCount);
        var trigger = Assert.Single(Assert.Single(source.Groups).TriggerSources);
        Assert.Equal("Trigger Item", trigger.Entity.Name);
        Assert.Equal(1, trigger.ApplicationCount);
        Assert.Equal(1, trigger.ObservedActivationBatchCount);
    }

    [Fact]
    public void Executed_context_collapses_target_fan_out_to_one_trigger()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    CardTarget("target"),
                    triggerSource: "trigger",
                    executionContextId: "trigger-context"
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    CardTarget("target-2"),
                    triggerSource: "trigger",
                    executionContextId: "trigger-context"
                )
            );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(1, source.ObservedActivationBatchCount);
        var trigger = Assert.Single(Assert.Single(source.Groups).TriggerSources);
        Assert.Equal(2, trigger.ApplicationCount);
        Assert.Equal(1, trigger.ObservedActivationBatchCount);
    }

    [Fact]
    public void Same_frame_effect_partitions_collapse_to_one_trigger_without_losing_targets()
    {
        var simulation = new CombatTimeline();
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        for (var index = 0; index < 10; index++)
        {
            var targetId = $"split-target-{index}";
            entities[targetId] = new CombatImpactEntity(
                targetId,
                $"Target {index}",
                "Item",
                null,
                index + 10
            );
            var executed = Executed(
                "source",
                CombatActionType.CardSlow,
                CardTarget(targetId),
                triggerSource: "trigger",
                executionContextId: index < 5 ? "opponent-partition" : "player-partition"
            );
            executed.EffectId = index < 5 ? "0" : "2";
            simulation.Frames[0].Events.Add(executed);
        }

        var source = Assert.Single(CombatImpactProjector.Project(simulation, entities).Sources);
        var slow = Assert.Single(source.Groups);

        Assert.Equal(10, source.EffectCount);
        Assert.Equal(10, slow.Count);
        Assert.Equal(10, slow.Targets.Count);
        Assert.Equal(1, source.ObservedActivationBatchCount);
        var trigger = Assert.Single(slow.TriggerSources);
        Assert.Equal(10, trigger.ApplicationCount);
        Assert.Equal(1, trigger.ObservedActivationBatchCount);
    }

    [Fact]
    public void Repair_destroy_transform_battles_can_create_wide_unique_target_groups()
    {
        const int useCount = 45;
        var simulation = new CombatTimeline();
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.UseCount] = useCount,
        };
        var entities = EntitiesWithSourceAttribute(CombatCardAttribute.HasteAmount, 1_000)
            .ToDictionary(item => item.Key, item => item.Value);

        for (var index = 0; index < useCount; index++)
        {
            var targetId = $"drone-{index}";
            entities[targetId] = new CombatImpactEntity(
                targetId,
                $"Small Drone {index}",
                "Item",
                null,
                index + 10
            );
            simulation
                .Frames[0]
                .Events.Add(Executed("source", CombatActionType.CardDestroy, CardTarget(targetId)));
            simulation
                .Frames[0]
                .Events.Add(
                    Executed(
                        "source",
                        CombatActionType.CardTransformDestroyed,
                        CardTarget(targetId)
                    )
                );
            simulation
                .Frames[0]
                .Events.Add(Executed("source", CombatActionType.CardRepair, CardTarget(targetId)));
            simulation
                .Frames[0]
                .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget(targetId)));
        }

        var source = Assert.Single(CombatImpactProjector.Project(simulation, entities).Sources);
        var groups = source.Groups.ToDictionary(group => group.NativeAttributeKey);

        Assert.Equal(useCount, source.UseCount);
        Assert.Equal(
            useCount,
            groups[CombatImpactAggregator.NativeKey(CombatImpactKind.Destroy)].Targets.Count
        );
        Assert.Equal(useCount, groups["TransformTargets"].Targets.Count);
        Assert.Equal(useCount, groups["RepairTargets"].Targets.Count);
        Assert.Equal(
            useCount,
            groups[CombatImpactAggregator.NativeKey(CombatImpactKind.Haste)].Targets.Count
        );
    }

    [Fact]
    public void Trigger_sources_are_attached_to_the_group_they_caused()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    CardTarget("target"),
                    triggerSource: "trigger"
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardHaste,
                    CardTarget("target"),
                    triggerSource: "target-2"
                )
            );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var slow = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Slow);
        var haste = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Haste);

        Assert.Equal("trigger", Assert.Single(slow.TriggerSources).Entity.Id);
        Assert.Equal("target-2", Assert.Single(haste.TriggerSources).Entity.Id);
    }

    [Fact]
    public void Mixed_self_and_external_trigger_sources_keep_self_to_reconcile_charge_counts()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardCharge,
                    CardTarget("source"),
                    triggerSource: "trigger"
                )
            );
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[1]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardCharge,
                    CardTarget("source"),
                    triggerSource: "source"
                )
            );
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[2]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardCharge,
                    CardTarget("source"),
                    triggerSource: "trigger"
                )
            );

        var source = Assert.Single(
            CombatImpactProjector
                .Project(
                    simulation,
                    EntitiesWithSourceAttribute(CombatCardAttribute.ChargeAmount, 1_000)
                )
                .Sources
        );
        var charge = Assert.Single(source.Groups);

        Assert.Equal(3, charge.Count);
        Assert.Equal(3, source.ObservedActivationBatchCount);
        Assert.Equal(3, charge.TriggerSources.Sum(trigger => trigger.ApplicationCount));
        Assert.Collection(
            charge.TriggerSources,
            trigger =>
            {
                Assert.Equal("Trigger Item", trigger.Entity.Name);
                Assert.Equal(2, trigger.ApplicationCount);
                Assert.Equal(2, trigger.ObservedActivationBatchCount);
            },
            self =>
            {
                Assert.Equal("Fairies", self.Entity.Name);
                Assert.Equal(1, self.ApplicationCount);
                Assert.Equal(1, self.ObservedActivationBatchCount);
            }
        );
    }

    [Fact]
    public void Same_trigger_source_in_separate_frames_counts_as_separate_activations()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    CardTarget("target"),
                    triggerSource: "trigger"
                )
            );
        simulation.Frames.Add(new CombatFrame());
        simulation
            .Frames[1]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    CardTarget("target-2"),
                    triggerSource: "trigger"
                )
            );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(2, source.ObservedActivationBatchCount);
        var trigger = Assert.Single(Assert.Single(source.Groups).TriggerSources);
        Assert.Equal(2, trigger.ApplicationCount);
        Assert.Equal(2, trigger.ObservedActivationBatchCount);
    }

    [Fact]
    public void Unclassified_executions_do_not_create_trigger_provenance()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatEffectExecuted
                {
                    ExecutionContextId = "ignored-context",
                    Source = ("source"),
                    TriggerSource = ("target-2"),
                    ActionType = CombatActionType.GameModifyTime,
                    Target = Player(Combatant.Opponent),
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardFreeze,
                    CardTarget("target"),
                    triggerSource: "trigger",
                    executionContextId: "executed-context"
                )
            );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(1, source.ObservedActivationBatchCount);
        Assert.Equal(
            "trigger",
            Assert.Single(Assert.Single(source.Groups).TriggerSources).Entity.Id
        );
    }

    [Fact]
    public void Trigger_source_fallback_is_not_mislabeled_as_self_trigger()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "effect",
                    CombatActionType.CardSlow,
                    CardTarget("target"),
                    triggerSource: "source"
                )
            );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var group = Assert.Single(source.Groups);

        Assert.Equal("source", source.Entity.Id);
        Assert.Empty(group.TriggerSources);
        Assert.Equal(1, group.TriggerFallbackApplicationCount);
        Assert.Equal(1, source.ObservedActivationBatchCount);
        Assert.Equal(
            CombatImpactTriggerPresentationState.BreakdownUnavailable,
            group.TriggerPresentationState
        );
    }

    [Fact]
    public void Unsupported_trigger_reference_is_an_explicit_unattributed_application()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardSlow,
                    CardTarget("target"),
                    triggerSource: "effect"
                )
            );

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var group = Assert.Single(source.Groups);

        Assert.Empty(group.TriggerSources);
        Assert.Equal(1, group.UnattributedTriggerApplicationCount);
        Assert.Equal(0, source.ObservedActivationBatchCount);
        Assert.Equal(
            CombatImpactTriggerPresentationState.BreakdownUnavailable,
            group.TriggerPresentationState
        );
    }

    [Fact]
    public void Filters_unclassified_actions_and_discloses_unresolved_targets()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatEffectExecuted
                {
                    Source = ("effect"),
                    TriggerSource = ("source"),
                    ActionType = CombatActionType.GameModifyTime,
                    Target = null!,
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    CombatActionType.CardDestroy,
                    CardTarget("missing-target"),
                    triggerSource: "trigger"
                )
            );

        var report = CombatImpactProjector.Project(simulation, Entities());

        var displayed = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(CombatImpactKind.Destroy, displayed.Kind);
        Assert.Equal(1, displayed.UnresolvedTargetCount);
    }

    [Theory]
    [InlineData(CombatDamageType.Regen, null)]
    [InlineData(CombatDamageType.Heal, 50)]
    public void Player_heal_matches_only_heal_adjustments(
        CombatDamageType damageType,
        int? expectedValue
    )
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.PlayerHeal, Player(Combatant.Player)));
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = damageType,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = 50,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.Healing, group.Kind);
        Assert.Equal(expectedValue, group.ObservedValue);
        Assert.Equal(
            expectedValue.HasValue ? CombatImpactCoverage.Exact : CombatImpactCoverage.None,
            group.ObservedCoverage
        );
    }

    [Fact]
    public void Reconstructs_lifesteal_from_unrecorded_health_gain_on_the_damage_frame()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("stinger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = Damage(-46);
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Health] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Health,
                    PreviousValue = 665,
                    CurrentValue = 706,
                },
            },
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Burn,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = -5,
                },
            },
        };
        var secondFrame = new CombatFrame
        {
            OpponentUpdates = Damage(-78),
            PlayerUpdates = new CombatPlayerUpdate
            {
                Attributes =
                {
                    [CombatPlayerAttribute.Health] = new CombatPlayerAttributeUpdate
                    {
                        AttributeType = CombatPlayerAttribute.Health,
                        PreviousValue = 239,
                        CurrentValue = 303,
                    },
                },
                HealthAdjustments =
                {
                    new CombatHealthAdjustment
                    {
                        DamageType = CombatDamageType.Burn,
                        AttributeChanged = CombatHealthChange.Health,
                        Amount = -14,
                    },
                },
            },
        };
        secondFrame.Events.Add(
            Executed("stinger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
        );
        simulation.Frames.Add(secondFrame);
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["stinger"] = new CombatImpactEntity(
            "stinger",
            "Stinger",
            "Item",
            null,
            5,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.Lifesteal] = 1,
            },
            CombatantId: Combatant.Player
        );

        var source = Assert.Single(
            CombatImpactProjector.Project(simulation, entities).Sources,
            candidate => candidate.Entity.Id == "stinger"
        );
        var healing = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Healing);

        Assert.Equal(2, healing.Count);
        Assert.Equal(124, healing.ObservedValue);
        Assert.Equal(CombatImpactOccurrenceBasis.ReconstructedTransition, healing.OccurrenceBasis);
        Assert.Equal(
            CombatImpactProjector.PlayerId(Combatant.Player),
            Assert.Single(healing.Targets).Entity.Id
        );
    }

    [Fact]
    public void Lifesteal_counts_only_realized_healing_when_the_damage_overheals()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("stinger", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = Damage(-78);
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Health] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Health,
                    PreviousValue = 730,
                    CurrentValue = 750,
                },
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["stinger"] = new CombatImpactEntity(
            "stinger",
            "Stinger",
            "Item",
            null,
            5,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.Lifesteal] = 1,
            },
            CombatantId: Combatant.Player
        );

        var source = Assert.Single(
            CombatImpactProjector.Project(simulation, entities).Sources,
            candidate => candidate.Entity.Id == "stinger"
        );
        var healing = Assert.Single(source.Groups, group => group.Kind == CombatImpactKind.Healing);

        Assert.Equal(20, healing.ObservedValue);
    }

    [Fact]
    public void Does_not_guess_lifesteal_ownership_when_multiple_sources_are_overheal_ambiguous()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("first", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("second", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                DamageAdjustment(CombatHealthChange.Health, -30, isCritical: false),
                DamageAdjustment(CombatHealthChange.Health, -20, isCritical: false),
            },
        };
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Health] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Health,
                    PreviousValue = 710,
                    CurrentValue = 750,
                },
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        foreach (var (id, order) in new[] { ("first", 5), ("second", 6) })
        {
            entities[id] = new CombatImpactEntity(
                id,
                id,
                "Item",
                null,
                order,
                Attributes: new Dictionary<CombatCardAttribute, int>
                {
                    [CombatCardAttribute.Lifesteal] = 1,
                },
                CombatantId: Combatant.Player
            );
        }

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.DoesNotContain(
            report.Sources.SelectMany(source => source.Groups),
            group => group.Kind == CombatImpactKind.Healing
        );
    }

    [Fact]
    public void Synthetic_zarlic_shape_keeps_self_only_haste_and_separate_application_count()
    {
        const string zarlic = "zarlic";
        const string decoy = "decoy";
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        for (var index = 0; index < 10; index++)
        {
            var frame = new CombatFrame();
            frame.Events.Add(
                Executed(
                    zarlic,
                    CombatActionType.CardHaste,
                    CardTarget(zarlic),
                    triggerSource: zarlic
                )
            );
            var target = (zarlic);
            frame.CardUpdates[target] = new CombatCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [CombatCardAttribute.Haste] = new CombatCardAttributeUpdate
                    {
                        AttributeType = CombatCardAttribute.Haste,
                        PreviousValue = 0,
                        CurrentValue = 950,
                    },
                },
            };
            var decoyTarget = (decoy);
            frame.CardUpdates[decoyTarget] = new CombatCardUpdate
            {
                CardInstanceId = decoyTarget,
                Attributes =
                {
                    [CombatCardAttribute.Haste] = new CombatCardAttributeUpdate
                    {
                        AttributeType = CombatCardAttribute.Haste,
                        PreviousValue = 1000,
                        CurrentValue = 950,
                    },
                },
            };
            simulation.Frames.Add(frame);
        }
        simulation.CardStats[zarlic] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.HastedCardsCount] = 10,
            [CombatCardStat.UseCount] = 10,
        };
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            [zarlic] = new(
                zarlic,
                "Zarlic",
                "Item",
                null,
                0,
                Attributes: new Dictionary<CombatCardAttribute, int>
                {
                    [CombatCardAttribute.HasteAmount] = 1_000,
                }
            ),
            [decoy] = new(decoy, "Other Food", "Item", null, 1),
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(report.Sources);
        var haste = Assert.Single(source.Groups);

        Assert.Equal(10, source.UseCount);
        Assert.Equal(10, source.EffectCount);
        Assert.Equal(10, source.ObservedActivationBatchCount);
        Assert.Equal(10, haste.Count);
        var selfTrigger = Assert.Single(haste.TriggerSources);
        Assert.Equal(zarlic, selfTrigger.Entity.Id);
        Assert.Equal(10, selfTrigger.ApplicationCount);
        Assert.Equal(
            CombatImpactTriggerPresentationState.HiddenSelfOnly,
            haste.TriggerPresentationState
        );
        Assert.Equal(10_000, haste.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, haste.ObservedCoverage);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, haste.Unit);
        Assert.Equal(10, haste.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactAuthoritativeBasis.ApplicationCount,
            haste.AuthoritativeMetric?.Basis
        );
        Assert.False(haste.ApplicationLedger.ComparableToAuthoritativeCount);
        Assert.Equal(
            CombatImpactControlStatus.NotComparable,
            haste.ApplicationLedger.ControlStatus
        );
        var targetRow = Assert.Single(haste.Targets);
        Assert.Equal("Zarlic", targetRow.Entity.Name);
        Assert.Equal(10, targetRow.Count);
        Assert.Equal(10_000, targetRow.ObservedValue);

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Haste, incoming.Kind);
        Assert.Equal(10, incoming.Count);
        Assert.Equal(10_000, incoming.ObservedValue);
        Assert.Equal(10, Assert.Single(incoming.Sources).Count);
    }

    [Fact]
    public void Positive_same_frame_haste_from_another_source_does_not_fan_out_targets()
    {
        var simulation = new CombatTimeline();
        var first = ("target");
        var second = ("target-2");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("trigger", CombatActionType.CardHaste, CardTarget("target-2")));
        simulation.Frames[0].CardUpdates[first] = HasteUpdate(first, 950);
        simulation.Frames[0].CardUpdates[second] = HasteUpdate(second, 1950);

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.HasteAmount] = 1_000,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.HasteAmount] = 2_000,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var firstGroup = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        var secondGroup = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );

        var firstTarget = Assert.Single(firstGroup.Targets);
        Assert.Equal("target", firstTarget.Entity.Id);
        Assert.Equal(1_000, firstTarget.ObservedValue);
        var secondTarget = Assert.Single(secondGroup.Targets);
        Assert.Equal("target-2", secondTarget.Entity.Id);
        Assert.Equal(2_000, secondTarget.ObservedValue);
    }

    [Fact]
    public void Configured_duration_is_not_lost_when_actions_share_a_frame_and_target()
    {
        var simulation = new CombatTimeline();
        var first = ("target");
        var second = ("target-2");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("trigger", CombatActionType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target-2")));
        simulation.Frames[0].CardUpdates[first] = HasteUpdate(first, 950);
        simulation.Frames[0].CardUpdates[second] = HasteUpdate(second, 1950);

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.HasteAmount] = 1_000,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.HasteAmount] = 2_000,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var fairies = Assert.Single(report.Sources, source => source.Entity.Id == "source");
        var trigger = Assert.Single(report.Sources, source => source.Entity.Id == "trigger");
        var fairiesGroup = Assert.Single(fairies.Groups);
        var triggerGroup = Assert.Single(trigger.Groups);

        Assert.Equal(2, fairiesGroup.Count);
        Assert.Equal(2_000, fairiesGroup.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, fairiesGroup.ObservedCoverage);
        Assert.All(fairiesGroup.Targets, target => Assert.Equal(1_000, target.ObservedValue));
        Assert.Equal(2_000, triggerGroup.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, triggerGroup.ObservedCoverage);
    }

    [Fact]
    public void Configured_duration_ignores_status_net_delta_when_decay_shares_the_frame()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.Haste] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.Haste,
                    PreviousValue = 1000,
                    CurrentValue = 1950,
                },
            },
        };

        var report = CombatImpactProjector.Project(
            simulation,
            EntitiesWithSourceAttribute(CombatCardAttribute.HasteAmount, 1_000)
        );
        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(1_000, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, group.ObservedCoverage);
    }

    [Fact]
    public void Configured_duration_actions_do_not_fall_back_to_target_frame_deltas()
    {
        var cases = new[]
        {
            (CombatActionType.CardCharge, CombatCardAttribute.Cooldown, CombatImpactKind.Charge),
            (CombatActionType.CardHaste, CombatCardAttribute.Haste, CombatImpactKind.Haste),
            (CombatActionType.CardSlow, CombatCardAttribute.Slow, CombatImpactKind.Slow),
            (CombatActionType.CardFreeze, CombatCardAttribute.Freeze, CombatImpactKind.Freeze),
        };
        foreach (var (action, targetAttribute, expectedKind) in cases)
        {
            var simulation = new CombatTimeline();
            var target = ("target");
            simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));
            simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [targetAttribute] = new CombatCardAttributeUpdate
                    {
                        AttributeType = targetAttribute,
                        PreviousValue = action == CombatActionType.CardCharge ? 5_000 : 1_000,
                        CurrentValue = action == CombatActionType.CardCharge ? 4_000 : 1_950,
                    },
                },
            };

            var report = CombatImpactProjector.Project(simulation, Entities());
            var group = Assert.Single(Assert.Single(report.Sources).Groups);
            var targetRow = Assert.Single(group.Targets);
            var incoming = Assert.Single(Assert.Single(report.Received).Groups);
            var incomingSource = Assert.Single(incoming.Sources);

            Assert.Equal(expectedKind, group.Kind);
            Assert.Equal(1, group.Count);
            Assert.Null(group.ObservedValue);
            Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
            Assert.Null(targetRow.ObservedValue);
            Assert.Null(incoming.ObservedValue);
            Assert.Null(incomingSource.ObservedValue);
        }
    }

    [Fact]
    public void Configured_duration_actions_use_one_source_value_in_both_perspectives()
    {
        var cases = new[]
        {
            (
                CombatActionType.CardCharge,
                CombatCardAttribute.ChargeAmount,
                CombatCardAttribute.Cooldown,
                CombatImpactKind.Charge
            ),
            (
                CombatActionType.CardHaste,
                CombatCardAttribute.HasteAmount,
                CombatCardAttribute.Haste,
                CombatImpactKind.Haste
            ),
            (
                CombatActionType.CardSlow,
                CombatCardAttribute.SlowAmount,
                CombatCardAttribute.Slow,
                CombatImpactKind.Slow
            ),
            (
                CombatActionType.CardFreeze,
                CombatCardAttribute.FreezeAmount,
                CombatCardAttribute.Freeze,
                CombatImpactKind.Freeze
            ),
        };
        foreach (var (action, sourceAttribute, targetAttribute, expectedKind) in cases)
        {
            var simulation = new CombatTimeline();
            var target = ("target");
            simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));
            simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [targetAttribute] = new CombatCardAttributeUpdate
                    {
                        AttributeType = targetAttribute,
                        PreviousValue = action == CombatActionType.CardCharge ? 5_000 : 1_000,
                        CurrentValue = action == CombatActionType.CardCharge ? 4_000 : 1_950,
                    },
                },
            };

            var report = CombatImpactProjector.Project(
                simulation,
                EntitiesWithSourceAttribute(sourceAttribute, 2_000)
            );
            var caused = Assert.Single(Assert.Single(report.Sources).Groups);
            var causedTarget = Assert.Single(caused.Targets);
            var received = Assert.Single(Assert.Single(report.Received).Groups);
            var receivedSource = Assert.Single(received.Sources);

            Assert.Equal(expectedKind, caused.Kind);
            Assert.Equal(CombatImpactAggregator.NativeKey(expectedKind), caused.NativeAttributeKey);
            Assert.Equal(2_000, caused.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Estimated, caused.ObservedCoverage);
            Assert.Equal(2_000, causedTarget.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Estimated, causedTarget.ObservedCoverage);
            Assert.Equal(2_000, received.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Estimated, received.ObservedCoverage);
            Assert.Equal(2_000, receivedSource.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Estimated, receivedSource.ObservedCoverage);
        }
    }

    [Fact]
    public void Configured_duration_uses_the_source_value_at_each_event_frame()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation.Frames.Add(SourceAttributeFrame(CombatCardAttribute.HasteAmount, 1_000, 2_000));
        var laterFrame = new CombatFrame();
        laterFrame.Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation.Frames.Add(laterFrame);

        var report = CombatImpactProjector.Project(
            simulation,
            EntitiesWithSourceAttribute(CombatCardAttribute.HasteAmount, 1_000)
        );
        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        var received = Assert.Single(Assert.Single(report.Received).Groups);

        Assert.Equal(2, caused.Count);
        Assert.Equal(3_000, caused.ObservedValue);
        Assert.Equal(3_000, Assert.Single(caused.Targets).ObservedValue);
        Assert.Equal(3_000, received.ObservedValue);
        Assert.Equal(3_000, Assert.Single(received.Sources).ObservedValue);
    }

    [Fact]
    public void Charge_uses_each_source_cards_configured_amount()
    {
        var simulation = new CombatTimeline();
        for (var index = 0; index < 4; index++)
            simulation
                .Frames[0]
                .Events.Add(Executed("finesse", CombatActionType.CardCharge, CardTarget("target")));
        for (var index = 0; index < 3; index++)
            simulation
                .Frames[0]
                .Events.Add(Executed("wok", CombatActionType.CardCharge, CardTarget("target")));

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["finesse"] = new CombatImpactEntity(
            "finesse",
            "Finesse Shield",
            "Skill",
            null,
            5,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.ChargeAmount] = 1_000,
            }
        );
        entities["wok"] = new CombatImpactEntity(
            "wok",
            "Jumbo Wok",
            "Item",
            null,
            6,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.ChargeAmount] = 2_000,
            }
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        var finesse = Assert.Single(report.Sources, source => source.Entity.Id == "finesse");
        var finesseCharge = Assert.Single(finesse.Groups);
        Assert.Equal(4, finesseCharge.Count);
        Assert.Equal(4_000, finesseCharge.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, finesseCharge.ObservedCoverage);

        var wok = Assert.Single(report.Sources, source => source.Entity.Id == "wok");
        var wokCharge = Assert.Single(wok.Groups);
        Assert.Equal(3, wokCharge.Count);
        Assert.Equal(6_000, wokCharge.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, wokCharge.ObservedCoverage);

        var incoming = Assert.Single(Assert.Single(report.Received).Groups);
        Assert.Equal(7, incoming.Count);
        Assert.Equal(10_000, incoming.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Estimated, incoming.ObservedCoverage);
        Assert.Equal(
            "×7 · 10s",
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false)
        );
        Assert.Equal(
            4_000,
            Assert.Single(incoming.Sources, source => source.Entity.Id == "finesse").ObservedValue
        );
        Assert.Equal(
            6_000,
            Assert.Single(incoming.Sources, source => source.Entity.Id == "wok").ObservedValue
        );
    }

    [Fact]
    public void Explicit_player_attribute_actions_preserve_signed_net_delta()
    {
        var cases = new[]
        {
            (CombatActionType.PlayerRegenApply, CombatPlayerAttribute.HealthRegen, 0, 12, 12),
            (CombatActionType.PlayerRageApply, CombatPlayerAttribute.Rage, 0, 9, 9),
            (
                CombatActionType.PlayerMaxHealthIncrease,
                CombatPlayerAttribute.HealthMax,
                100,
                120,
                20
            ),
            (
                CombatActionType.PlayerMaxHealthDecrease,
                CombatPlayerAttribute.HealthMax,
                120,
                100,
                -20
            ),
        };
        foreach (var (action, attribute, previous, current, expected) in cases)
        {
            var simulation = new CombatTimeline();
            simulation.Frames[0].Events.Add(Executed("source", action, Player(Combatant.Player)));
            simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
            {
                Attributes =
                {
                    [attribute] = new CombatPlayerAttributeUpdate
                    {
                        AttributeType = attribute,
                        PreviousValue = previous,
                        CurrentValue = current,
                    },
                },
            };

            var group = Assert.Single(
                Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
            );

            Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
            Assert.Equal(expected, group.ObservedValue);
            Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
        }
    }

    [Fact]
    public void Applied_regen_and_card_regen_gain_from_one_source_remain_distinct()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerRegenApply, Player(Combatant.Player))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.HealthRegen] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.HealthRegen,
                    PreviousValue = 0,
                    CurrentValue = 338,
                },
            },
        };
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.RegenApplyAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.RegenApplyAmount,
                    PreviousValue = 0,
                    CurrentValue = 16,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.RegenAdded] = 338,
        };

        var groups = Assert
            .Single(
                CombatImpactProjector
                    .Project(
                        simulation,
                        EntitiesWithSourceAttribute(CombatCardAttribute.RegenApplyAmount, 4)
                    )
                    .Sources
            )
            .Groups;

        var applied = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.AppliedEffect
        );
        Assert.Equal("RegenApplyAmount", applied.NativeAttributeKey);
        Assert.Equal(338, applied.AuthoritativeMetric?.Value);
        Assert.Equal(0, applied.CriticalCount);

        var cardGain = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.CardAttribute
        );
        Assert.Equal("RegenApplyAmount", cardGain.NativeAttributeKey);
        Assert.Null(cardGain.AuthoritativeMetric);
        Assert.Equal(16, cardGain.ObservedValue);
        Assert.Equal(0, cardGain.CriticalCount);
        Assert.Equal("target", Assert.Single(cardGain.Targets).Entity.Id);
        Assert.All(groups, group => Assert.Equal(0, group.CriticalCount));
    }

    [Theory]
    [InlineData(
        CombatActionType.PlayerMaxHealthIncrease,
        CombatActionType.PlayerMaxHealthDecrease,
        Combatant.Player,
        CombatPlayerAttribute.HealthMax,
        100,
        110
    )]
    [InlineData(
        CombatActionType.PlayerBurnApply,
        CombatActionType.PlayerBurnRemove,
        Combatant.Opponent,
        CombatPlayerAttribute.Burn,
        10,
        16
    )]
    public void Competing_player_actions_do_not_split_one_transition(
        CombatActionType firstAction,
        CombatActionType secondAction,
        Combatant target,
        CombatPlayerAttribute attribute,
        int previous,
        int current
    )
    {
        var simulation = new CombatTimeline();
        simulation.Frames[0].Events.Add(Executed("source", firstAction, Player(target)));
        simulation.Frames[0].Events.Add(Executed("trigger", secondAction, Player(target)));
        var update = new CombatPlayerUpdate
        {
            Attributes =
            {
                [attribute] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = previous,
                    CurrentValue = current,
                },
            },
        };
        if (target == Combatant.Player)
            simulation.Frames[0].PlayerUpdates = update;
        else
            simulation.Frames[0].OpponentUpdates = update;

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.NotEmpty(report.Sources);
        Assert.All(
            report.Sources.SelectMany(source => source.Groups),
            group => Assert.Null(group.ObservedValue)
        );
    }

    [Fact]
    public void Generic_card_modifier_does_not_block_a_specific_attribute_transition()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", CombatActionType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = HasteUpdate(target, 950);

        var report = CombatImpactProjector.Project(
            simulation,
            EntitiesWithSourceAttribute(CombatCardAttribute.HasteAmount, 1_000)
        );

        Assert.Equal(
            1_000,
            Assert
                .Single(
                    Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
                )
                .ObservedValue
        );
        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "trigger");
    }

    [Fact]
    public void Residual_does_not_duplicate_a_transition_claimed_by_another_action_kind()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", CombatActionType.CardHaste, CardTarget("target")));
        simulation.Frames[0].Events.Add(AttributeExecution("trigger", "haste-gain", "target"));
        simulation.Frames[0].CardUpdates[target] = HasteUpdate(target, 950);

        var entities = EntitiesWithSourceAttribute(CombatCardAttribute.HasteAmount, 1_000)
            .ToDictionary(item => item.Key, item => item.Value);
        entities["trigger"] = entities["trigger"] with
        {
            AbilityAttributeTypesByEffectId = new Dictionary<string, CombatCardAttribute>
            {
                ["haste-gain"] = CombatCardAttribute.Haste,
            },
            AbilityAttributeModifiersByEffectId = new Dictionary<string, CombatAttributeModifier>
            {
                ["haste-gain"] = new CombatAttributeModifier
                {
                    AttributeType = CombatCardAttribute.Haste,
                    Operation = CombatAttributeOperation.Add,
                    Value = new CombatFixedValue { Value = 50 },
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        var source = Assert.Single(report.Sources);
        var haste = Assert.Single(source.Groups);
        Assert.Equal("source", source.Entity.Id);
        Assert.Equal(CombatImpactKind.Haste, haste.Kind);
        Assert.Equal(1_000, haste.ObservedValue);
        var incoming = Assert.Single(Assert.Single(report.Received).Groups);
        Assert.Equal(CombatImpactKind.Haste, incoming.Kind);
        Assert.Null(incoming.TransitionLedger);
    }

    [Fact]
    public void Unknown_player_modifier_prevents_assigning_the_whole_delta_to_a_specific_action()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerRageApply, Player(Combatant.Player))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "trigger",
                    CombatActionType.PlayerModifyAttribute,
                    Player(Combatant.Player)
                )
            );
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.Rage] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.Rage,
                    PreviousValue = 0,
                    CurrentValue = 9,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.Null(
            Assert
                .Single(
                    Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
                )
                .ObservedValue
        );
        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "trigger");
    }

    [Fact]
    public void Generic_player_modifier_uses_the_only_transition_not_claimed_by_specific_actions()
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerRegenApply, Player(Combatant.Player))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.PlayerModifyAttribute, Player(Combatant.Player))
            );
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.HealthRegen] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.HealthRegen,
                    PreviousValue = 2,
                    CurrentValue = 4,
                },
                [CombatPlayerAttribute.HealthMax] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.HealthMax,
                    PreviousValue = 780,
                    CurrentValue = 790,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var groups = Assert.Single(report.Sources).Groups;

        Assert.Equal(2, groups.Count);
        // The generic action has no metadata here: it may also contribute to Regen.
        // Only the otherwise-unclaimed max-health transition is identifiable.
        Assert.Null(
            Assert
                .Single(groups, group => group.NativeAttributeKey == "RegenApplyAmount")
                .ObservedValue
        );
        var maxHealth = Assert.Single(
            groups,
            group => group.NativeAttributeKey == CombatPlayerAttribute.HealthMax.ToString()
        );
        Assert.Equal(10, maxHealth.ObservedValue);
        Assert.Equal(CombatImpactEventSurface.PlayerAttribute, maxHealth.Surface);
    }

    [Fact]
    public void Max_health_increase_and_decrease_use_distinct_directional_group_keys()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var increaseFrame = new CombatFrame();
        increaseFrame.Events.Add(
            Executed("source", CombatActionType.PlayerMaxHealthIncrease, Player(Combatant.Player))
        );
        increaseFrame.PlayerUpdates = new CombatPlayerUpdate
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
        simulation.Frames.Add(increaseFrame);
        var decreaseFrame = new CombatFrame();
        decreaseFrame.Events.Add(
            Executed("source", CombatActionType.PlayerMaxHealthDecrease, Player(Combatant.Player))
        );
        decreaseFrame.PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [CombatPlayerAttribute.HealthMax] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = CombatPlayerAttribute.HealthMax,
                    PreviousValue = 130,
                    CurrentValue = 120,
                },
            },
        };
        simulation.Frames.Add(decreaseFrame);

        var groups = Assert
            .Single(CombatImpactProjector.Project(simulation, Entities()).Sources)
            .Groups;

        Assert.Equal(2, groups.Count);
        Assert.Equal(
            30,
            Assert
                .Single(groups, group => group.NativeAttributeKey == "HealthMaxIncrease")
                .ObservedValue
        );
        Assert.Equal(
            -10,
            Assert
                .Single(groups, group => group.NativeAttributeKey == "HealthMaxDecrease")
                .ObservedValue
        );
    }

    [Fact]
    public void Quantified_and_unquantified_reload_occurrences_share_one_group_key()
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var quantified = new CombatFrame();
        quantified.Events.Add(
            Executed("source", CombatActionType.CardReload, CardTarget("target"))
        );
        var target = ("target");
        quantified.CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.Ammo] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.Ammo,
                    PreviousValue = 2,
                    CurrentValue = 5,
                },
            },
        };
        simulation.Frames.Add(quantified);
        var unquantified = new CombatFrame();
        unquantified.Events.Add(
            Executed("source", CombatActionType.CardReload, CardTarget("target"))
        );
        simulation.Frames.Add(unquantified);

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("ReloadAmount", group.NativeAttributeKey);
        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
        Assert.Equal(2, group.Count);
        Assert.Equal(3, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Partial, group.ObservedCoverage);
    }

    [Theory]
    [InlineData(CombatCardAttribute.DamageAmount, 187, "Amount")]
    [InlineData(CombatCardAttribute.FlyingTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.DestroyTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.ForceUseTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.EnchantTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.UpgradeTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.DisableTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.RepairTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.TransformTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.EnchantRemoveTargets, 2, "Amount")]
    [InlineData(CombatCardAttribute.ChargeAmount, 1500, "Milliseconds")]
    [InlineData(CombatCardAttribute.HasteAmount, 1500, "Milliseconds")]
    [InlineData(CombatCardAttribute.SlowAmount, 1500, "Milliseconds")]
    [InlineData(CombatCardAttribute.FreezeAmount, 1500, "Milliseconds")]
    [InlineData(CombatCardAttribute.FlatCooldownReduction, 1500, "Milliseconds")]
    [InlineData(CombatCardAttribute.Lifesteal, 12, "PercentagePoints")]
    [InlineData(CombatCardAttribute.TempoCost, 2, "Amount")]
    [InlineData(CombatCardAttribute.FlatTempoCostReduction, 1, "Amount")]
    [InlineData(CombatCardAttribute.PercentTempoCostReduction, 20, "PercentagePoints")]
    public void Pr132_card_modifier_attributes_and_units_are_preserved(
        CombatCardAttribute attributeType,
        int value,
        string expectedUnit
    )
    {
        var group = ProjectSingleAuraAttribute(attributeType, value);

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(CombatImpactEventSurface.CardAttribute, group.Surface);
        Assert.Equal(CombatImpactOccurrenceBasis.ReconstructedTransition, group.OccurrenceBasis);
        Assert.Equal(attributeType.ToString(), group.NativeAttributeKey);
        Assert.Equal(expectedUnit, group.Unit.ToString());
        Assert.Equal(value, group.ObservedValue);
        Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
    }

    [Fact]
    public void Tempo_amount_attributes_and_units_are_preserved()
    {
        var cases = new[]
        {
            (Attribute: CombatCardAttribute.TempoApplyAmount, Value: 3),
            (Attribute: CombatCardAttribute.TempoRemoveAmount, Value: 2),
        };
        foreach (var (attribute, value) in cases)
        {
            var group = ProjectSingleAuraAttribute(attribute, value);

            Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
            Assert.Equal(CombatImpactEventSurface.CardAttribute, group.Surface);
            Assert.Equal(attribute.ToString(), group.NativeAttributeKey);
            Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
            Assert.Equal(value, group.ObservedValue);
            Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
        }
    }

    [Fact]
    public void Aura_attribute_change_is_omitted_when_multiple_sources_claim_one_transition()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted { Source = ("source"), AppliedTo = { CardTarget("target") } }
            );
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    Source = ("trigger"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        Assert.Empty(CombatImpactProjector.Project(simulation, Entities()).Sources);
    }

    [Fact]
    public void Self_reference_aura_recalculations_are_not_counted_as_combat_impact()
    {
        var simulation = new CombatTimeline();
        var self = ("source");
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    EffectId = "health-formula",
                    Source = self,
                    AppliedTo = { CardTarget("source") },
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    EffectId = "value-formula",
                    Source = ("trigger"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[self] = new CombatCardUpdate
        {
            CardInstanceId = self,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 6,
                    CurrentValue = 7_018,
                },
            },
        };
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.CritChance] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.CritChance,
                    PreviousValue = 64,
                    CurrentValue = 82,
                },
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            ReferenceValuedAuraEffectIds = ["health-formula"],
        };
        entities["trigger"] = entities["trigger"] with
        {
            AuraAttributeTypesByEffectId = new Dictionary<string, CombatCardAttribute>
            {
                ["value-formula"] = CombatCardAttribute.CritChance,
            },
            ReferenceValuedAuraEffectIds = ["value-formula"],
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "source");
        Assert.DoesNotContain(report.Received, received => received.Entity.Id == "source");
        var crit = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );
        Assert.Equal("CritChance", crit.NativeAttributeKey);
        Assert.Equal(18, crit.ObservedValue);
        Assert.Equal("target", Assert.Single(crit.Targets).Entity.Id);
    }

    [Theory]
    [InlineData(CombatPlayerAttribute.HealthMax, "Amount")]
    [InlineData(CombatPlayerAttribute.CritChance, "PercentagePoints")]
    [InlineData(CombatPlayerAttribute.DamageCrit, "Amount")]
    [InlineData(CombatPlayerAttribute.HealAmount, "Amount")]
    [InlineData(CombatPlayerAttribute.ShieldCrit, "Amount")]
    [InlineData(CombatPlayerAttribute.FlatDamageReduction, "Amount")]
    [InlineData(CombatPlayerAttribute.RageMax, "Amount")]
    [InlineData(CombatPlayerAttribute.Tempo, "Amount")]
    [InlineData(CombatPlayerAttribute.TempoGainCooldownMax, "Milliseconds")]
    [InlineData(CombatPlayerAttribute.FlatTempoGainCooldownReduction, "Milliseconds")]
    [InlineData(CombatPlayerAttribute.PercentTempoGainCooldownReduction, "PercentagePoints")]
    [InlineData(CombatPlayerAttribute.Gold, "Amount")]
    [InlineData(CombatPlayerAttribute.RerollCostModifier, "Amount")]
    public void Pr132_player_modifier_and_economy_aura_attributes_are_preserved(
        CombatPlayerAttribute attributeType,
        string expectedUnit
    )
    {
        var simulation = new CombatTimeline();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted
                {
                    Source = ("source"),
                    AppliedTo = { Player(Combatant.Player) },
                }
            );
        simulation.Frames[0].PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [attributeType] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = attributeType,
                    PreviousValue = 0,
                    CurrentValue = 3,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(attributeType.ToString(), group.NativeAttributeKey);
        Assert.Equal(3, group.ObservedValue);
        Assert.Equal(expectedUnit, group.Unit.ToString());
        Assert.Equal(
            CombatImpactProjector.PlayerId(Combatant.Player),
            Assert.Single(group.Targets).Entity.Id
        );
    }

    [Fact]
    public void Unresolved_aura_overlap_preserves_the_transition_without_assigning_it_to_the_execution()
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted { Source = ("source"), AppliedTo = { CardTarget("target") } }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", CombatActionType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var source = Assert.Single(report.Sources);
        var group = Assert.Single(source.Groups);

        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.True(group.HasUnattributedTransitionValue);
        Assert.Equal(187, Assert.Single(report.AttributeTransitionDiagnostics).ResidualValue);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(-20)]
    public void Aura_teardown_does_not_offset_the_applied_contribution(int grantedValue)
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var source = ("source");
        var target = ("target");

        var applied = new CombatFrame();
        applied.Events.Add(
            new CombatAuraExecuted
            {
                EffectId = "persistent-aura",
                Source = source,
                AppliedTo = { CardTarget("target") },
            }
        );
        applied.CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.CritChance] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.CritChance,
                    PreviousValue = 0,
                    CurrentValue = grantedValue,
                },
            },
        };
        simulation.Frames.Add(applied);

        var teardown = new CombatFrame();
        teardown.Events.Add(
            new CombatAuraExecuted
            {
                EffectId = "persistent-aura",
                Source = source,
                RemovedFrom = { CardTarget("target") },
            }
        );
        teardown.Events.Add(new CombatantDied { CombatantId = Combatant.Opponent });
        teardown.CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.CritChance] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.CritChance,
                    PreviousValue = grantedValue,
                    CurrentValue = 0,
                },
            },
        };
        simulation.Frames.Add(teardown);

        var report = CombatImpactProjector.Project(simulation, Entities());
        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        var received = Assert.Single(Assert.Single(report.Received).Groups);

        Assert.Equal(1, caused.Count);
        Assert.Equal(grantedValue, caused.ObservedValue);
        Assert.Equal(1, received.Count);
        Assert.Equal(grantedValue, received.ObservedValue);
    }

    private static CombatImpactGroup ProjectSingleAuraAttribute(
        CombatCardAttribute attributeType,
        int currentValue
    )
    {
        var simulation = new CombatTimeline();
        var target = ("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatAuraExecuted { Source = ("source"), AppliedTo = { CardTarget("target") } }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [attributeType] = new CombatCardAttributeUpdate
                {
                    AttributeType = attributeType,
                    PreviousValue = 0,
                    CurrentValue = currentValue,
                },
            },
        };

        return Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );
    }

    private static CombatEffectExecuted Executed(
        string source,
        CombatActionType action,
        CombatTarget target,
        string? triggerSource = null,
        string executionContextId = "",
        string? effectId = null
    ) =>
        new()
        {
            ExecutionContextId = executionContextId,
            Source = (source),
            TriggerSource = triggerSource == null ? null : (triggerSource),
            EffectId = effectId ?? string.Empty,
            ActionType = action,
            Target = target,
        };

    private static CombatEffectExecuted AttributeExecution(
        string source,
        string effectId,
        string target
    )
    {
        var executed = Executed(source, CombatActionType.CardModifyAttribute, CardTarget(target));
        executed.EffectId = effectId;
        return executed;
    }

    private static CombatCardUpdate DamageUpdate(
        string target,
        int previousValue,
        int currentValue
    ) =>
        new()
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.DamageAmount] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.DamageAmount,
                    PreviousValue = previousValue,
                    CurrentValue = currentValue,
                },
            },
        };

    private static CombatImpactEntity AttributeGainEntity(
        string id,
        string name,
        int configuredValue,
        string effectId,
        int order
    )
    {
        var modifier = new CombatAttributeModifier
        {
            AttributeType = CombatCardAttribute.DamageAmount,
            Operation = CombatAttributeOperation.Add,
            Value = new CombatCardAttributeReference
            {
                AttributeType = CombatCardAttribute.Custom_0,
                TargetsSelf = true,
            },
        };
        return new CombatImpactEntity(
            id,
            name,
            id == "bass" ? "Item" : "Skill",
            null,
            order,
            Attributes: new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.Custom_0] = configuredValue,
            },
            AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                [effectId] = CombatCardAttribute.DamageAmount,
            },
            AbilityAttributeModifiersByEffectId: new Dictionary<string, CombatAttributeModifier>
            {
                [effectId] = modifier,
            }
        );
    }

    private static CombatImpactEntity AttributeModifierEntity(
        string id,
        string name,
        string effectId,
        CombatAttributeModifier modifier,
        int order
    ) =>
        new(
            id,
            name,
            "Skill",
            null,
            order,
            AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                [effectId] = modifier.AttributeType,
            },
            AbilityAttributeModifiersByEffectId: new Dictionary<string, CombatAttributeModifier>
            {
                [effectId] = modifier,
            }
        );

    private static void AssertDamageGain(
        CombatImpactReport report,
        string sourceId,
        int expectedCount,
        int expectedValue
    ) => AssertAttributeGain(report, sourceId, "bass", expectedCount, expectedValue);

    private static void AssertAttributeGain(
        CombatImpactReport report,
        string sourceId,
        string targetId,
        int expectedCount,
        int expectedValue
    )
    {
        var source = Assert.Single(report.Sources, candidate => candidate.Entity.Id == sourceId);
        var group = Assert.Single(source.Groups);
        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal("DamageAmount", group.NativeAttributeKey);
        Assert.Equal(expectedCount, group.Count);
        Assert.Equal(expectedValue, group.ObservedValue);
        var target = Assert.Single(group.Targets);
        Assert.Equal(targetId, target.Entity.Id);
        Assert.Equal(expectedCount, target.Count);
        Assert.Equal(expectedValue, target.ObservedValue);
    }

    private static CombatCardTransformed Transformed(
        string originalId,
        string transformedId,
        Combatant combatantId
    ) => new() { OriginalInstanceId = originalId, TransformedCardInstanceIds = [transformedId] };

    private static CombatCardActionCostSpent TempoCostSpent(string sourceId) =>
        new() { ExecutingCard = sourceId, PlayerAttributeSpent = CombatPlayerAttribute.Tempo };

    private static CombatPlayerTarget Player(Combatant target) => new() { Target = target };

    private static CombatCardTarget CardTarget(string target) => new() { Target = (target) };

    private static CombatImpactEntity CriticalTriggerEntity(CombatEffectPriority priority) =>
        new(
            "crit-listener",
            "Boombox",
            "Item",
            null,
            5,
            AbilityAttributeTypesByEffectId: new Dictionary<string, CombatCardAttribute>
            {
                ["crit-gain"] = CombatCardAttribute.DamageAmount,
            },
            CriticalTriggerAbilitiesByEffectId: new Dictionary<string, CombatEffectPriority>
            {
                ["crit-gain"] = priority,
            }
        );

    private static CombatImpactReport ProjectCriticalTriggerScalingCase(int evidenceCount)
    {
        var simulation = new CombatTimeline();
        simulation.Frames.Clear();
        var target = ("target");
        for (var index = 0; index < evidenceCount; index++)
        {
            var frame = PlayerEffectFrame(
                CombatActionType.PlayerBurnApply,
                CombatPlayerAttribute.Burn,
                (index + 1) * 3,
                (index + 2) * 3
            );
            frame.Events.Add(
                Executed(
                    "crit-listener",
                    CombatActionType.CardModifyAttribute,
                    CardTarget("target"),
                    triggerSource: "source",
                    executionContextId: $"critical-listener-{index}",
                    effectId: "crit-gain"
                )
            );
            frame.CardUpdates[target] = DamageUpdate(target, index, index + 1);
            simulation.Frames.Add(frame);
        }
        simulation.CardStats["source"] = new Dictionary<CombatCardStat, int>
        {
            [CombatCardStat.BurnAdded] = evidenceCount * 3,
        };

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<CombatCardAttribute, int>
            {
                [CombatCardAttribute.BurnApplyAmount] = 3,
            },
            CritCapableEffectIds = ["critical-effect"],
        };
        entities["crit-listener"] = CriticalTriggerEntity(CombatEffectPriority.Low);
        return CombatImpactProjector.Project(simulation, entities);
    }

    private static CombatPlayerUpdate Damage(int amount) =>
        new()
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = amount,
                },
            },
        };

    private static CombatFrame DamageFrame(int healthAmount, int shieldAmount, bool isCritical)
    {
        var frame = new CombatFrame();
        frame.Events.Add(
            Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
        );
        frame.OpponentUpdates = new CombatPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Health,
                    Amount = healthAmount,
                    IsCrit = isCritical,
                },
                new CombatHealthAdjustment
                {
                    DamageType = CombatDamageType.Damage,
                    AttributeChanged = CombatHealthChange.Shield,
                    Amount = shieldAmount,
                    IsCrit = isCritical,
                },
            },
        };
        return frame;
    }

    private static CombatFrame ConcurrentDamageFrame(bool isCritical, bool splitFirstHit = false)
    {
        var frame = new CombatFrame { OpponentUpdates = new CombatPlayerUpdate() };
        for (var effect = 0; effect < 3; effect++)
        {
            frame.Events.Add(
                Executed("source", CombatActionType.PlayerDamage, Player(Combatant.Opponent))
            );
        }

        if (splitFirstHit)
        {
            frame.OpponentUpdates.HealthAdjustments.Add(
                DamageAdjustment(CombatHealthChange.Health, -10, isCritical)
            );
            frame.OpponentUpdates.HealthAdjustments.Add(
                DamageAdjustment(CombatHealthChange.Shield, -30, isCritical)
            );
        }
        else
        {
            frame.OpponentUpdates.HealthAdjustments.Add(
                DamageAdjustment(CombatHealthChange.Health, -30, isCritical)
            );
        }
        frame.OpponentUpdates.HealthAdjustments.Add(
            DamageAdjustment(CombatHealthChange.Health, isCritical ? -80 : -30, isCritical)
        );
        frame.OpponentUpdates.HealthAdjustments.Add(
            DamageAdjustment(CombatHealthChange.Health, isCritical ? -80 : -30, isCritical)
        );
        return frame;
    }

    private static CombatHealthAdjustment DamageAdjustment(
        CombatHealthChange attribute,
        int amount,
        bool isCritical
    ) =>
        new()
        {
            DamageType = CombatDamageType.Damage,
            AttributeChanged = attribute,
            Amount = amount,
            IsCrit = isCritical,
        };

    private static CombatCardUpdate HasteUpdate(string target, int amount) =>
        new()
        {
            CardInstanceId = target,
            Attributes =
            {
                [CombatCardAttribute.Haste] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.Haste,
                    PreviousValue = 0,
                    CurrentValue = amount,
                },
            },
        };

    private static CombatFrame PlayerEffectFrame(
        CombatActionType action,
        CombatPlayerAttribute attribute,
        int previous,
        int current,
        string effectId = "critical-effect",
        string? triggerSource = null
    )
    {
        var frame = new CombatFrame();
        frame.Events.Add(
            Executed("source", action, Player(Combatant.Player), triggerSource, effectId: effectId)
        );
        frame.PlayerUpdates = new CombatPlayerUpdate
        {
            Attributes =
            {
                [attribute] = new CombatPlayerAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = previous,
                    CurrentValue = current,
                },
            },
        };
        return frame;
    }

    private static CombatFrame SourceAttributeFrame(
        CombatCardAttribute attribute,
        int previous,
        int current
    )
    {
        var frame = new CombatFrame();
        SetSourceAttributeUpdate(frame, attribute, previous, current);
        return frame;
    }

    private static CombatFrame ValueAndCritGainFrame(int frameIndex)
    {
        var frame = new CombatFrame();
        foreach (var targetId in new[] { "soul", "crit-target", "value-target" })
        {
            var executed = Executed(
                "pen",
                CombatActionType.CardModifyAttribute,
                CardTarget(targetId)
            );
            executed.ExecutionContextId = $"pen-context-{frameIndex}";
            executed.EffectId = "0";
            frame.Events.Add(executed);
        }

        frame.Events.Add(
            new CombatAuraExecuted
            {
                ExecutionContextId = $"caviar-context-{frameIndex}",
                EffectId = "1",
                Source = ("caviar"),
                AppliedTo = { CardTarget("soul"), CardTarget("crit-target") },
            }
        );

        foreach (var targetId in new[] { "soul", "crit-target", "value-target" })
        {
            var target = (targetId);
            var update = new CombatCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [CombatCardAttribute.SellPrice] = new CombatCardAttributeUpdate
                    {
                        AttributeType = CombatCardAttribute.SellPrice,
                        PreviousValue = frameIndex * 3,
                        CurrentValue = (frameIndex + 1) * 3,
                    },
                },
            };
            if (targetId != "value-target")
            {
                update.Attributes[CombatCardAttribute.CritChance] = new CombatCardAttributeUpdate
                {
                    AttributeType = CombatCardAttribute.CritChance,
                    PreviousValue = frameIndex * 6,
                    CurrentValue = (frameIndex + 1) * 6,
                };
            }
            frame.CardUpdates[target] = update;
        }

        return frame;
    }

    private static void SetSourceAttributeUpdate(
        CombatFrame frame,
        CombatCardAttribute attribute,
        int previous,
        int current
    )
    {
        var source = ("source");
        frame.CardUpdates[source] = new CombatCardUpdate
        {
            CardInstanceId = source,
            Attributes =
            {
                [attribute] = new CombatCardAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = previous,
                    CurrentValue = current,
                },
            },
        };
    }

    private static void AddPlayerEffectFrames(
        CombatTimeline simulation,
        CombatPlayerAttribute attribute,
        params int[] values
    )
    {
        for (var index = 1; index < values.Length; index++)
        {
            simulation.Frames.Add(
                PlayerEffectFrame(
                    CombatActionType.PlayerBurnApply,
                    attribute,
                    values[index - 1],
                    values[index]
                )
            );
        }
    }

    private static IReadOnlyDictionary<string, CombatImpactEntity> EntitiesWithSourceAttribute(
        CombatCardAttribute attribute,
        int value
    ) => EntitiesWithSourceAttributes((attribute, value));

    private static IReadOnlyDictionary<string, CombatImpactEntity> EntitiesWithSourceAttributes(
        params (CombatCardAttribute Attribute, int Value)[] attributes
    )
    {
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = attributes.ToDictionary(item => item.Attribute, item => item.Value),
            CritCapableEffectIds = ["critical-effect"],
        };
        return entities;
    }

    private static Dictionary<string, CombatImpactEntity> TempoAttributionEntities(
        CombatImpactItemTagCondition itemCondition
    )
    {
        var skillTemplateId = Guid.Parse("a6a75fbb-864b-4875-815e-e00cf3fe40bd");
        var sourceRule = new CombatImpactPrerequisiteSkillSourceRule(
            "minor-tempo",
            skillTemplateId,
            [CardTier.Gold]
        );
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["minor-skill"] = new CombatImpactEntity(
            "minor-skill",
            "A Minor",
            "Skill",
            null,
            5,
            skillTemplateId,
            CardTier.Gold,
            CombatantId: Combatant.Player
        );
        entities["note-effect"] = new CombatImpactEntity(
            "note-effect",
            "A Note Socket Effect",
            "SocketEffect",
            null,
            6,
            DisplaySpan: 1,
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_6,
            Section: CardSection.Hand,
            PrerequisiteSkillSourceRulesByEffectId: new Dictionary<
                string,
                CombatImpactPrerequisiteSkillSourceRule
            >
            {
                [sourceRule.EffectId] = sourceRule,
            },
            UseAttributionRules: [new CombatImpactUseAttributionRule(sourceRule, itemCondition, 2)]
        );
        entities["qualifying-item"] = new CombatImpactEntity(
            "qualifying-item",
            "Weapon",
            "Item",
            null,
            7,
            DisplaySpan: 2,
            CombatantId: Combatant.Player,
            SocketId: CardSocket.Socket_5,
            Section: CardSection.Hand,
            Tags: [CardTag.Weapon]
        );
        return entities;
    }

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
