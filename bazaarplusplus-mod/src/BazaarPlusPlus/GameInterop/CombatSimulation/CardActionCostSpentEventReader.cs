#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;

namespace BazaarPlusPlus.GameInterop.CombatSimulation;

internal static class CardActionCostSpentEventReader
{
    internal const string RuntimeTypeName =
        "BazaarGameShared.Infra.Messages.CombatSimEvents.CombatSimEventCardActionCostSpent";

    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;

    internal static bool TryRead(ICombatSimEvent? candidate, out CardActionCostSpentEvent value)
    {
        value = default;
        if (candidate == null)
            return false;

        var runtimeType = candidate.GetType();
        if (!string.Equals(runtimeType.FullName, RuntimeTypeName, StringComparison.Ordinal))
            return false;

        try
        {
            var executingCardProperty = runtimeType.GetProperty("ExecutingCard", InstancePublic);
            var playerAttributeProperty = runtimeType.GetProperty(
                "PlayerAttributeSpent",
                InstancePublic
            );
            if (
                executingCardProperty?.GetValue(candidate) is not InstanceId executingCard
                || playerAttributeProperty == null
            )
                return false;

            var playerAttributeValue = playerAttributeProperty.GetValue(candidate);
            if (playerAttributeValue != null && playerAttributeValue is not EPlayerAttributeType)
                return false;

            value = new CardActionCostSpentEvent(
                executingCard,
                playerAttributeValue is EPlayerAttributeType playerAttribute
                    ? playerAttribute
                    : null
            );
            return true;
        }
        catch
        {
            // This event is absent from Production and optional on newer builds. A shape mismatch
            // must degrade only this metric instead of preventing the combat report from opening.
            return false;
        }
    }
}

internal readonly record struct CardActionCostSpentEvent(
    InstanceId ExecutingCard,
    EPlayerAttributeType? PlayerAttributeSpent
);
