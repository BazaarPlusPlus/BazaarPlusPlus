#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.CollectionPanel;

// The enlarged Collection card can sit beneath a wide cooldown tooltip. Move only those
// Collection item tooltips to the right; native positioning still handles screen-edge fallback.
[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowCardTooltipController)
)]
internal static class CollectionCooldownTooltipPositionPatch
{
    private const float ExtraRightOffsetPixels = 96f;

    [HarmonyPrefix]
    private static void Prefix(
        Transform worldSpaceTransform,
        ref Vector3 worldSpaceOffset,
        ITooltipData tooltipData,
        bool parentInScreenSpace
    )
    {
        if (
            !parentInScreenSpace
            || worldSpaceTransform == null
            || worldSpaceTransform.GetComponent<CollectionPanelOwnedMarker>() == null
            || tooltipData is not CardTooltipData { CardInstance: ItemCard } cardTooltipData
            || !HasCooldown(cardTooltipData)
        )
            return;

        worldSpaceOffset.x += ExtraRightOffsetPixels;
    }

    private static bool HasCooldown(CardTooltipData tooltipData)
    {
        var cooldown = tooltipData.GetCurrentAttributeValue(ECardAttributeType.CooldownMax);
        return cooldown.HasValue && cooldown.Value > 0f;
    }
}
