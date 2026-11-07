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

        AssertTagCount(InventoryFrom(card), ECardTag.Toy.ToString(), 1);
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

        AssertTagCount(InventoryFrom(card), tag, 1);
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

        AssertTagCount(InventoryFrom(card), tag, 1);
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

        AssertTagCount(InventoryFrom(card), "Toy", 0);
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

        AssertTagCount(InventoryFrom(card), "Toy", 1);
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

    private static void AssertTagCount(EncounterInventory inventory, string tag, int expectedCount)
    {
        Assert.True(
            new EncounterCardRequirement(
                [],
                [
                    [tag],
                ],
                "Any",
                "Equal",
                expectedCount
            ).Matches(inventory)
        );
    }
}
