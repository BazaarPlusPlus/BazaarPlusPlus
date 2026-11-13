using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Tooltips;
using Xunit;

namespace TooltipPreviewTargetResolver.Tests;

// Each test is one check the capsule ran, with the same calls and messages. The capsule shared
// one item card across its checks; each test builds the card the check saw.
public sealed class TooltipPreviewTargetSelectionTests
{
    [Fact]
    public void Matching_item_tooltip_data_is_accepted()
    {
        var itemCard = Item("item-a");
        var itemTooltipData = CreateTooltipData(itemCard);

        Assert(
            TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
                itemTooltipData,
                itemCard
            ) == itemTooltipData,
            "Matching item tooltip data should be accepted."
        );
    }

    [Fact]
    public void Missing_current_card_falls_through_to_the_active_tooltip()
    {
        var itemTooltipData = CreateTooltipData(Item("item-a"));

        Assert(
            TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
                itemTooltipData,
                null
            ) == itemTooltipData,
            "Missing current card should fall through to the active tooltip (modifier-driven refresh case)."
        );
    }

    [Fact]
    public void Mismatched_current_card_is_rejected()
    {
        var itemTooltipData = CreateTooltipData(Item("item-a"));
        var otherItemCard = Item("item-b");

        Assert(
            TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
                itemTooltipData,
                otherItemCard
            ) == null,
            "Mismatched current card should be rejected."
        );
    }

    [Fact]
    public void Cards_with_the_same_instance_id_are_the_same_target()
    {
        var itemCard = Item("item-a");
        var itemTooltipData = CreateTooltipData(itemCard);
        var clonedItemCard = Item("same-item");
        itemCard.InstanceId = new InstanceId("same-item");

        Assert(
            TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
                itemTooltipData,
                clonedItemCard
            ) == itemTooltipData,
            "Cards with the same instance id should be treated as the same tooltip target."
        );
    }

    [Fact]
    public void Non_item_tooltip_data_is_rejected()
    {
        var nonItemCard = new Card { Type = ECardType.Skill };
        var nonItemTooltipData = CreateTooltipData(nonItemCard);

        Assert(
            TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
                nonItemTooltipData,
                nonItemCard
            ) == null,
            "Non-item tooltip data should be rejected."
        );
    }

    [Fact]
    public void Missing_tooltip_data_is_rejected()
    {
        var itemCard = Item("same-item");

        Assert(
            TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(null, itemCard)
                == null,
            "Missing tooltip data should be rejected."
        );
    }

    private static ItemCard Item(string instanceId) =>
        new()
        {
            Type = ECardType.Item,
            Section = EInventorySection.Hand,
            InstanceId = new InstanceId(instanceId),
        };

    private static TheBazaar.Tooltips.CardTooltipData CreateTooltipData(Card card)
    {
        var tooltipData = (TheBazaar.Tooltips.CardTooltipData)
            RuntimeHelpers.GetUninitializedObject(typeof(TheBazaar.Tooltips.CardTooltipData));
        typeof(TheBazaar.Tooltips.CardTooltipData)
            .GetField("_cardInstance", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tooltipData, card);
        typeof(TheBazaar.Tooltips.CardTooltipData)
            .GetField("_cardTemplate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tooltipData, card is ItemCard ? new TCardItem() : new TCardSkill());
        return tooltipData;
    }

    private static void Assert(bool condition, string message) =>
        Xunit.Assert.True(condition, message);
}
