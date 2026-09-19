using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.GameInterop.Cards;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class CardAbilityValueReaderTests
{
    [Fact]
    public void Live_evaluation_uses_the_current_player_attribute_and_native_modifier()
    {
        var run = RunWithPlayerAttribute(EPlayerAttributeType.Custom_8, 3);
        var template = Template(
            new TReferenceValuePlayerAttribute
            {
                AttributeType = EPlayerAttributeType.Custom_8,
                Target = new TTargetPlayerAbsolute
                {
                    TargetMode = ETargetPlayerAbsoluteTargetMode.Player,
                },
                Modifier = MultiplyBy(2f),
            },
            EPlayerAttributeType.Prestige
        );

        Assert.True(
            CardAbilityValueReader.TryEvaluate(template, "0", new ValueContext(run), out var value)
        );
        Assert.Equal("6", value.ValueText);
    }

    [Fact]
    public void Live_evaluation_counts_matching_cards_in_hand_and_stash()
    {
        var run = RunWithPlayerAttribute(EPlayerAttributeType.Gold, 0);
        AddCard(run.Player, toStash: false, Guid.NewGuid(), ECardTag.Friend);
        AddCard(run.Player, toStash: true, Guid.NewGuid(), ECardTag.Relic);
        AddCard(run.Player, toStash: true, Guid.NewGuid(), ECardTag.Tool);
        var template = Template(
            new TReferenceValueCardCount
            {
                Target = new TTargetCardSection
                {
                    TargetSection = ETargetCardSectionTargetSection.AbsolutePlayerHandAndStash,
                    Conditions = new TCardConditionalTag
                    {
                        Tags = new HashSet<ECardTag> { ECardTag.Friend, ECardTag.Relic },
                        Operator = EListComparisonOperator.Any,
                    },
                },
                Modifier = MultiplyBy(5f),
            },
            EPlayerAttributeType.Gold
        );

        Assert.True(
            CardAbilityValueReader.TryEvaluate(template, "0", new ValueContext(run), out var value)
        );
        Assert.Equal("10", value.ValueText);
    }

    [Fact]
    public void Live_evaluation_supports_nested_card_attribute_aggregates()
    {
        var locketId = Guid.Parse("d641c57b-ab42-4b2f-b4f4-addd18eb7100");
        var run = RunWithPlayerAttribute(EPlayerAttributeType.HealthMax, 1000);
        var locket = AddCard(run.Player, toStash: false, locketId, ECardTag.Relic);
        locket.Attributes[ECardAttributeType.Custom_7] = 4;
        var percentPerUse = new TReferenceValueCardAttributeAggregate
        {
            AttributeType = ECardAttributeType.Custom_7,
            Target = new TTargetCardSection
            {
                TargetSection = ETargetCardSectionTargetSection.AbsolutePlayerHandAndStash,
                Conditions = new TCardConditionalId { Id = locketId },
            },
            Modifier = MultiplyBy(0.01f, shouldRound: false),
        };
        var template = Template(
            new TReferenceValuePlayerAttributeUnscaled
            {
                AttributeType = EPlayerAttributeType.HealthMax,
                Target = new TTargetPlayerAbsolute
                {
                    TargetMode = ETargetPlayerAbsoluteTargetMode.Player,
                },
                Modifier = new TValueModifier
                {
                    ModifyMode = EValueModifierMode.Multiply,
                    Value = percentPerUse,
                    ShouldRound = true,
                },
            },
            EPlayerAttributeType.HealthMax
        );

        Assert.True(
            CardAbilityValueReader.TryEvaluate(template, "0", new ValueContext(run), out var value)
        );
        Assert.Equal("40", value.ValueText);
    }

    private static TCardEncounterStep Template(ITValue value, EPlayerAttributeType attributeType) =>
        new()
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["0"] = new TCardAbility
                {
                    Action = new TActionPlayerModifyAttribute
                    {
                        AttributeType = attributeType,
                        Value = value,
                    },
                },
            },
        };

    private static Run RunWithPlayerAttribute(EPlayerAttributeType attributeType, int value) =>
        new()
        {
            Player = new Player
            {
                Attributes = new Dictionary<EPlayerAttributeType, int> { [attributeType] = value },
            },
        };

    private static ItemCard AddCard(Player player, bool toStash, Guid templateId, ECardTag tag)
    {
        var card = new ItemCard
        {
            TemplateId = templateId,
            Size = ECardSize.Small,
            Tags = new HashSet<ECardTag> { tag },
            Owner = player,
        };
        var container = Assert.IsType<CardContainer>(toStash ? player.Stash : player.Hand);
        var socket = Array.FindIndex(container.Container.Sockets, entry => entry == null);
        Assert.True(socket >= 0);
        container.Container.Sockets[socket] = card;
        return card;
    }

    private static TValueModifier MultiplyBy(float value, bool shouldRound = true) =>
        new()
        {
            ModifyMode = EValueModifierMode.Multiply,
            Value = new TFixedValue { Value = value },
            ShouldRound = shouldRound,
        };
}
