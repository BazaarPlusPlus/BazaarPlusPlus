using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.PlayerEffects;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEntitySnapshotReaderTests
{
    [Fact]
    public void Hidden_tags_fall_back_to_the_template_for_rehydrated_replay_cards()
    {
        var card = RehydratedItem(templateTags: [], templateHiddenTags: [EHiddenTag.Haste]);

        Assert.Contains(EHiddenTag.Haste, CombatImpactEntityTags.ResolveHiddenTags(card)!);
    }

    [Fact]
    public void Public_tags_fall_back_to_the_template_for_rehydrated_replay_cards()
    {
        var card = RehydratedItem(templateTags: [ECardTag.Weapon], templateHiddenTags: []);

        Assert.Contains(ECardTag.Weapon, CombatImpactEntityTags.ResolveTags(card)!);
    }

    [Fact]
    public void Direct_card_attribute_modifier_preserves_its_configured_value_graph()
    {
        var action = new TActionCardModifyAttribute
        {
            AttributeType = ECardAttributeType.DamageAmount,
            Operation = EAttributeModifierOperation.Add,
            Value = new TFixedValue { Value = 100 },
        };

        var modifiers = CombatImpactAbilityAttributeModifierReader.Read([
            new TCardAbility { Id = "damage-gain", Action = action },
        ]);

        Assert.Same(action, Assert.Single(modifiers!).Value);
    }

    [Fact]
    public void Conflicting_modifiers_with_the_same_effect_id_are_not_snapshotted()
    {
        var modifiers = CombatImpactAbilityAttributeModifierReader.Read([
            new TCardAbility
            {
                Id = "ambiguous",
                Action = new TActionCardModifyAttribute
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    Operation = EAttributeModifierOperation.Add,
                    Value = new TFixedValue { Value = 100 },
                },
            },
            new TCardAbility
            {
                Id = "ambiguous",
                Action = new TActionCardModifyAttribute
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    Operation = EAttributeModifierOperation.Add,
                    Value = new TFixedValue { Value = 4 },
                },
            },
        ]);

        Assert.Null(modifiers);
    }

    [Fact]
    public void Active_abilities_include_the_current_enchantments_crit_trigger()
    {
        var baseAbility = new TCardAbility { Id = "base" };
        var enchantedCrit = new TCardAbility
        {
            Id = "e1",
            Trigger = new TTriggerOnCardCritted(),
            Priority = EEffectPriority.Medium,
        };
        var item = new ItemCard
        {
            Enchantment = EEnchantmentType.Fiery,
            Template = new TCardItem
            {
                Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
                {
                    [EEnchantmentType.Fiery] = new()
                    {
                        Abilities = new Dictionary<string, TCardAbility>
                        {
                            [enchantedCrit.Id] = enchantedCrit,
                        },
                    },
                },
            },
        };

        var abilities = CombatImpactActiveAbilityReader.Read(item, [baseAbility]);
        var critTriggers = CombatImpactCriticalTriggerReader.Read(abilities);

        Assert.Equal(["base", "e1"], abilities.Select(ability => ability.Id));
        Assert.Equal(EEffectPriority.Medium, critTriggers?["e1"]);
    }

    [Fact]
    public void Combat_only_player_effect_is_reconstructed_from_a_unique_complete_signature()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("implicit"),
                    EffectId = "2",
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("implicit", "trigger", "0", EActionCommandType.CardModifyAttribute)
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("implicit", "trigger", "1", EActionCommandType.CardModifyAttribute)
            );
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["trigger"] = Entity("trigger", ECombatantId.Player, 0),
            ["target"] = Entity("target", ECombatantId.Player, 1),
        };
        var template = PlayerEffectTemplate(Guid.NewGuid());

        var added = CombatImpactImplicitPlayerEffectReader.AddMissing(
            simulation,
            entities,
            [template]
        );

        Assert.Equal(1, added);
        var implicitEntity = entities["implicit"];
        Assert.Equal(template.Id, implicitEntity.TemplateId);
        Assert.Equal(10, implicitEntity.Attributes?[ECardAttributeType.Custom_0]);
        Assert.Equal(
            ECardAttributeType.Slow,
            implicitEntity.AbilityAttributeModifiersByEffectId?["0"].AttributeType
        );
        Assert.Equal(
            ECardAttributeType.PercentCooldownReduction,
            implicitEntity.AuraAttributeTypesByEffectId?["2"]
        );
    }

    [Fact]
    public void Ambiguous_combat_only_player_effect_signature_stays_unresolved()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("implicit", "trigger", "0", EActionCommandType.CardModifyAttribute)
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("implicit", "trigger", "1", EActionCommandType.CardModifyAttribute)
            );
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("implicit"),
                    EffectId = "2",
                    AppliedTo = { CardTarget("target") },
                }
            );
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["trigger"] = Entity("trigger", ECombatantId.Player, 0),
            ["target"] = Entity("target", ECombatantId.Player, 1),
        };

        var added = CombatImpactImplicitPlayerEffectReader.AddMissing(
            simulation,
            entities,
            [PlayerEffectTemplate(Guid.NewGuid()), PlayerEffectTemplate(Guid.NewGuid())]
        );

        Assert.Equal(0, added);
        Assert.DoesNotContain("implicit", entities.Keys);
    }

    [Fact]
    public void Transformed_replay_cards_retain_their_creation_update_for_rehydration()
    {
        var simulation = new CombatSim();
        var transformed = new SimEventCardTransformation(
            "transformed",
            "16b0d645-3a47-45a1-be2a-1e3ee311f33e",
            ECardType.Item,
            ECombatantId.Player,
            EInventorySection.Hand,
            EContainerSocketId.Socket_2
        );
        simulation
            .Frames[0]
            .Events.Add(new CombatSimEventCardTransformed("context", "original", [transformed]));
        var transformedId = InstanceId.TryParse(transformed.InstanceId);
        simulation.Frames[0].CardUpdates[transformedId] = new CombatSimCardUpdate
        {
            CardInstanceId = transformedId,
            Tags = [],
            HiddenTags = [],
            Tier = ETier.Gold,
        };
        var snapshot = Assert.Single(CombatImpactTransformedCardSnapshotReader.Read(simulation));

        Assert.Equal("transformed", snapshot.Card.InstanceId);
        Assert.Equal(ECombatantId.Player, snapshot.Card.CombatantId);
        Assert.Equal(EInventorySection.Hand, snapshot.Card.Section);
        Assert.Equal(EContainerSocketId.Socket_2, snapshot.Card.Socket);
        Assert.Equal(ETier.Gold, snapshot.Update?.Tier);
        Assert.Empty(snapshot.Update?.Tags!);
        Assert.Empty(snapshot.Update?.HiddenTags!);
    }

    [Fact]
    public void Transform_revert_snapshots_include_each_incarnation_once()
    {
        var simulation = new CombatSim();
        var original = new SimEventCardTransformation(
            "original",
            Guid.NewGuid().ToString(),
            ECardType.Item,
            ECombatantId.Player,
            EInventorySection.Hand,
            EContainerSocketId.Socket_2
        );
        var transformed = original with
        {
            InstanceId = "transformed",
            TemplateId = Guid.NewGuid().ToString(),
        };
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventCardTransformed("context", original.InstanceId, [transformed])
            );
        simulation.Frames.Add(new CombatSimFrame());
        simulation
            .Frames[1]
            .Events.Add(
                new CombatSimEventCardTransformReverted(original, [transformed.InstanceId])
            );

        var snapshots = CombatImpactTransformedCardSnapshotReader.Read(simulation);

        Assert.Equal(["transformed", "original"], snapshots.Select(item => item.Card.InstanceId));
    }

    [Theory]
    [InlineData("<style=Radiant>Radiant</style>\nHunter's Journal")]
    [InlineData("<style=Radiant>Hunter's Journal</style>")]
    public void Hunter_journal_never_leaks_native_style_tags(string nativeTitle) =>
        Assert.Equal(
            "Hunter's Journal",
            CombatImpactEntityName.RemoveNativeEnchantmentPrefix(nativeTitle)
        );

    private static ItemCard RehydratedItem(
        IReadOnlyCollection<ECardTag> templateTags,
        IReadOnlyCollection<EHiddenTag> templateHiddenTags
    ) =>
        new()
        {
            Type = ECardType.Item,
            Tags = [],
            HiddenTags = [],
            Template = new TCardItem
            {
                Type = ECardType.Item,
                Tags = templateTags.ToHashSet(),
                HiddenTags = templateHiddenTags.ToHashSet(),
            },
        };

    private static TCardPlayerEffect PlayerEffectTemplate(Guid id)
    {
        var first = new TCardAbility
        {
            Id = "0",
            Action = new TActionCardModifyAttribute
            {
                AttributeType = ECardAttributeType.Slow,
                Operation = EAttributeModifierOperation.Multiply,
                Value = new TFixedValue { Value = 0 },
            },
        };
        var second = new TCardAbility
        {
            Id = "1",
            Action = new TActionCardModifyAttribute
            {
                AttributeType = ECardAttributeType.Freeze,
                Operation = EAttributeModifierOperation.Multiply,
                Value = new TFixedValue { Value = 0 },
            },
        };
        var aura = new TCardAura
        {
            Id = "2",
            Action = new TAuraActionCardModifyAttribute
            {
                AttributeType = ECardAttributeType.PercentCooldownReduction,
                Operation = EAttributeModifierOperation.Add,
                Value = new TReferenceValueCardAttribute
                {
                    AttributeType = ECardAttributeType.Custom_0,
                    Target = new BazaarGameShared.Domain.Targeting.TTargetCardSelf(),
                },
            },
        };
        return new TCardPlayerEffect
        {
            Id = id,
            InternalName = "Base Rage Effect",
            StartingTier = ETier.Diamond,
            Abilities = new Dictionary<string, TCardAbility>
            {
                [first.Id] = first,
                [second.Id] = second,
            },
            Auras = new Dictionary<string, TCardAura> { [aura.Id] = aura },
            Tiers = new Dictionary<ETier, BazaarGameShared.Domain.Cards.TCardTier>
            {
                [ETier.Diamond] = new()
                {
                    Attributes = new Dictionary<ECardAttributeType, int>
                    {
                        [ECardAttributeType.Custom_0] = 10,
                    },
                    AbilityIds = [first.Id, second.Id],
                    AuraIds = [aura.Id],
                },
            },
        };
    }

    private static CombatImpactEntity Entity(string id, ECombatantId combatant, int order) =>
        new(id, id, "Skill", null, order, CombatantId: combatant);

    private static CombatSimEventEffectExecuted Executed(
        string source,
        string trigger,
        string effectId,
        EActionCommandType actionType
    ) =>
        new()
        {
            Source = InstanceId.TryParse(source),
            TriggerSource = InstanceId.TryParse(trigger),
            EffectId = effectId,
            ActionType = actionType,
            Target = CardTarget("target"),
        };

    private static EffectTargetCard CardTarget(string id) =>
        new() { Target = InstanceId.TryParse(id) };
}
