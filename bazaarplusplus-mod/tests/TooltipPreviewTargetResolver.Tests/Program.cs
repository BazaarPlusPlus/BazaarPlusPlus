using System.Reflection;
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Tooltips;

var itemCard = new ItemCard
{
    Type = ECardType.Item,
    Section = EInventorySection.Hand,
    InstanceId = new InstanceId("item-a"),
};
var itemTooltipData = CreateTooltipData(itemCard);

Assert(
    TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(itemTooltipData, itemCard)
        == itemTooltipData,
    "Matching item tooltip data should be accepted."
);

Assert(
    TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(itemTooltipData, null)
        == itemTooltipData,
    "Missing current card should fall through to the active tooltip (modifier-driven refresh case)."
);

var otherItemCard = new ItemCard
{
    Type = ECardType.Item,
    Section = EInventorySection.Hand,
    InstanceId = new InstanceId("item-b"),
};

Assert(
    TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
        itemTooltipData,
        otherItemCard
    ) == null,
    "Mismatched current card should be rejected."
);

var clonedItemCard = new ItemCard
{
    Type = ECardType.Item,
    Section = EInventorySection.Hand,
    InstanceId = new InstanceId("same-item"),
};
itemCard.InstanceId = new InstanceId("same-item");

Assert(
    TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
        itemTooltipData,
        clonedItemCard
    ) == itemTooltipData,
    "Cards with the same instance id should be treated as the same tooltip target."
);

var nonItemCard = new Card { Type = ECardType.Skill };
var nonItemTooltipData = CreateTooltipData(nonItemCard);

Assert(
    TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
        nonItemTooltipData,
        nonItemCard
    ) == null,
    "Non-item tooltip data should be rejected."
);

Assert(
    TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(null, itemCard) == null,
    "Missing tooltip data should be rejected."
);

Console.WriteLine("TooltipPreviewTargetResolver checks passed.");

static TheBazaar.Tooltips.CardTooltipData CreateTooltipData(Card card)
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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
