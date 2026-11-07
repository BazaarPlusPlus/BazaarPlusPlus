using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterInventoryProducerTests
{
    [Fact]
    public void Runtime_only_tags_reach_encounter_requirements()
    {
        var card = Item();
        card.Tags = [ECardTag.Toy];

        AssertHasTag(InventoryFrom(card), ECardTag.Toy.ToString());
    }

    [Theory]
    [InlineData("Drone")]
    [InlineData("Freeze")]
    public void Template_only_tags_reach_encounter_requirements(string tag)
    {
        var card = Item();
        card.Template = new TCardItem
        {
            Type = ECardType.Item,
            Tags = [ECardTag.Drone],
            HiddenTags = [EHiddenTag.Freeze],
        };

        AssertHasTag(InventoryFrom(card), tag);
    }

    [Theory]
    [InlineData("Toy")]
    [InlineData("Freeze")]
    public void Active_enchantment_tags_reach_encounter_requirements(string tag)
    {
        var card = Item();
        card.Enchantment = EEnchantmentType.Fiery;
        card.Template = new TCardItem
        {
            Type = ECardType.Item,
            Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
            {
                [EEnchantmentType.Fiery] = new()
                {
                    Tags = [ECardTag.Toy],
                    HiddenTags = [EHiddenTag.Freeze],
                },
            },
        };

        AssertHasTag(InventoryFrom(card), tag);
    }

    [Fact]
    public void Inactive_enchantment_tags_do_not_reach_encounter_requirements()
    {
        var card = Item();
        card.Enchantment = EEnchantmentType.Heavy;
        card.Template = new TCardItem
        {
            Type = ECardType.Item,
            Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
            {
                [EEnchantmentType.Fiery] = new() { Tags = [ECardTag.Toy] },
            },
        };

        Assert.Equal(
            0,
            InventoryFrom(card)
                .CountMatchingTags(
                    [
                        ["Toy"],
                    ],
                    "Any"
                )
        );
    }

    [Fact]
    public void Duplicate_tags_on_one_card_count_once()
    {
        var card = Item();
        card.Tags = [ECardTag.Toy];
        card.Enchantment = EEnchantmentType.Fiery;
        card.Template = new TCardItem
        {
            Type = ECardType.Item,
            Tags = [ECardTag.Toy],
            Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
            {
                [EEnchantmentType.Fiery] = new() { Tags = [ECardTag.Toy] },
            },
        };

        Assert.Equal(
            1,
            InventoryFrom(card)
                .CountMatchingTags(
                    [
                        ["Toy"],
                    ],
                    "Any"
                )
        );
    }

    private static ItemCard Item() =>
        new()
        {
            TemplateId = Guid.NewGuid(),
            Type = ECardType.Item,
            Tags = [],
            HiddenTags = [],
        };

    private static EncounterInventory InventoryFrom(ItemCard card)
    {
        var cards = new List<EncounterInventoryCard>();
        var addCards = typeof(EncounterPreviewGameRuntime).GetMethod(
            "AddCards",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(addCards);
        addCards.Invoke(null, [cards, new Card[] { card }]);
        return new EncounterInventory(cards);
    }

    private static void AssertHasTag(EncounterInventory inventory, string tag)
    {
        Assert.Equal(
            1,
            inventory.CountMatchingTags(
                [
                    [tag],
                ],
                "Any"
            )
        );
        Assert.True(
            new EncounterCardRequirement(
                [],
                [
                    [tag],
                ],
                "Any",
                "GreaterThanOrEqual",
                1
            ).Matches(inventory)
        );
    }
}
