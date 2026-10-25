#nullable enable

namespace BazaarPlusPlus.Core.Cards.LiveCards;

public enum LiveCardSnapshotSection
{
    All,
    Board,
    Stash,
}

public enum LiveCardSnapshotFailureReason
{
    ReadException,
    InvalidPlacement,
}

/// <summary>
/// How a degraded line renders <c>socket_id</c> and <c>item_size</c>: snake case
/// (<c>socket_3</c>, <c>large</c>) on the desktop, the member name (<c>Socket_3</c>,
/// <c>Large</c>) on Android, each host's existing monitoring format.
/// </summary>
public enum LiveCardSnapshotLogValues
{
    SnakeCase,
    MemberName,
}

/// <summary>
/// A card <see cref="LiveCardSnapshotRead"/> left out, or a read that failed. The size is the one
/// the fit check used, after the Small fallback.
/// </summary>
public sealed record LiveCardSnapshotIssue(
    LiveCardSnapshotSection Section,
    LiveCardSnapshotFailureReason Reason,
    Guid? TemplateId,
    int? Socket,
    CardSize? Size,
    Exception? Exception
);

public sealed class LiveCardSnapshotReadOutcome
{
    internal LiveCardSnapshotReadOutcome(
        LiveCardSnapshotSet snapshot,
        IReadOnlyList<LiveCardSnapshotIssue> issues
    )
    {
        Snapshot = snapshot;
        Issues = issues;
    }

    public LiveCardSnapshotSet Snapshot { get; }

    public IReadOnlyList<LiveCardSnapshotIssue> Issues { get; }
}
