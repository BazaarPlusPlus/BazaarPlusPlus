#nullable enable
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal readonly record struct CollectionCardMapLoadOutcome(
    object? Source,
    Dictionary<Guid, ITCard>? Map,
    CollectionPanelLogReasonCode? FailureReason,
    Exception? Exception
)
{
    internal bool IsAvailable => Source != null && Map != null && !FailureReason.HasValue;

    internal static CollectionCardMapLoadOutcome From(
        object? source,
        Task<Dictionary<Guid, ITCard>?>? task
    )
    {
        if (source == null || task == null)
        {
            return new CollectionCardMapLoadOutcome(
                source,
                null,
                CollectionPanelLogReasonCode.StaticDataNotReady,
                null
            );
        }

        if (task.IsFaulted)
        {
            return new CollectionCardMapLoadOutcome(
                source,
                null,
                CollectionPanelLogReasonCode.CardMapTaskFailed,
                task.Exception?.GetBaseException()
            );
        }

        if (task.IsCanceled)
        {
            return new CollectionCardMapLoadOutcome(
                source,
                null,
                CollectionPanelLogReasonCode.CardMapTaskFailed,
                new TaskCanceledException(task)
            );
        }

        var map = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
        return map == null
            ? new CollectionCardMapLoadOutcome(
                source,
                null,
                CollectionPanelLogReasonCode.CardMapNull,
                null
            )
            : new CollectionCardMapLoadOutcome(source, map, null, null);
    }
}
