#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public enum BazaarAgentActionObservationStatus
{
    Pending,
    Confirmed,
    TimedOut,
}

public static class BazaarAgentActionObservation
{
    public static BazaarAgentActionObservationStatus Evaluate(
        BazaarAgentContext baseline,
        BazaarAgentContext? current,
        double nowSeconds,
        double deadlineSeconds
    )
    {
        if (
            current is not null
            && BazaarAgentContextSnapshotPublisher.HasGameplayStateChanged(baseline, current)
        )
            return BazaarAgentActionObservationStatus.Confirmed;
        return nowSeconds >= deadlineSeconds
            ? BazaarAgentActionObservationStatus.TimedOut
            : BazaarAgentActionObservationStatus.Pending;
    }

    public static BazaarAgentActionObservationStatus Evaluate(
        BazaarAgentContext baseline,
        BazaarAgentContext? current,
        BazaarAgentAction action,
        double nowSeconds,
        double deadlineSeconds
    )
    {
        if (current is not null && HasExpectedEffect(baseline, current, action))
        {
            return BazaarAgentActionObservationStatus.Confirmed;
        }

        return nowSeconds >= deadlineSeconds
            ? BazaarAgentActionObservationStatus.TimedOut
            : BazaarAgentActionObservationStatus.Pending;
    }

    private static bool HasExpectedEffect(
        BazaarAgentContext baseline,
        BazaarAgentContext current,
        BazaarAgentAction action
    ) =>
        action.ActionKind switch
        {
            BazaarAgentActionKind.SellItem => !ContainsCard(current, action.CardInstanceId),
            BazaarAgentActionKind.MoveItem => IsAtTarget(current, action),
            _ => BazaarAgentContextSnapshotPublisher.HasGameplayStateChanged(baseline, current),
        };

    private static bool ContainsCard(BazaarAgentContext context, string? instanceId) =>
        context
            .BoardItems.Concat(context.ChestItems)
            .Concat(context.PlayerSkills)
            .Any(card => card.InstanceId == instanceId);

    private static bool IsAtTarget(BazaarAgentContext context, BazaarAgentAction action)
    {
        var targetLocation =
            action.TargetSection == BazaarAgentTargetSection.Hand
                ? BazaarAgentCardLocation.Board
                : BazaarAgentCardLocation.Chest;
        var card = context
            .BoardItems.Concat(context.ChestItems)
            .FirstOrDefault(card => card.InstanceId == action.CardInstanceId);
        return card is not null
            && card.Location == targetLocation
            && action.TargetSockets is { Count: > 0 }
            && card.SocketId == action.TargetSockets[0];
    }
}
