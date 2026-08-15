#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.BazaarAgentHost;

/// <summary>
/// Restores the final card-face refresh that the physical pedestal drop normally reaches through
/// its visual graph. Agent commands have no dragged <see cref="ItemController"/>, so the graph
/// can leave the card frame at its old tier even after the authoritative card data changed.
/// </summary>
internal sealed class BazaarAgentPedestalVisualRefresh : MonoBehaviour
{
    private string? _pendingInstanceId;

    private void Awake()
    {
        Events.CardUpgradedSimEvent.AddListener(OnCardUpgraded, this);
        Events.CardEnchantedSimEvent.AddListener(OnCardEnchanted, this);
    }

    internal void Arm(ItemCard card) => _pendingInstanceId = card.InstanceId.Value;

    internal void Cancel(ItemCard card)
    {
        if (string.Equals(_pendingInstanceId, card.InstanceId.Value, StringComparison.Ordinal))
            _pendingInstanceId = null;
    }

    private void OnDestroy()
    {
        Events.CardUpgradedSimEvent.RemoveListener(OnCardUpgraded);
        Events.CardEnchantedSimEvent.RemoveListener(OnCardEnchanted);
        _pendingInstanceId = null;
    }

    private void OnCardUpgraded(GameSimEventCardUpgraded update) => Refresh(update.InstanceId);

    private void OnCardEnchanted(GameSimEventCardEnchanted update) => Refresh(update.InstanceId);

    private void Refresh(string? instanceId)
    {
        if (
            string.IsNullOrWhiteSpace(instanceId)
            || !string.Equals(_pendingInstanceId, instanceId, StringComparison.Ordinal)
        )
            return;

        _pendingInstanceId = null;
        if (!Data.Entities.TryGetValue(InstanceId.TryParse(instanceId), out var card))
            return;
        if (Data.CardAndSkillLookup?.GetCardController(card) is not ItemController controller)
            return;

        // The event is raised after PedestalState applies the server's SimUpdateCard, so this
        // always reads the newly upgraded/enchanted tier and attributes.
        controller.UpdateCardFromEvent();
    }
}
