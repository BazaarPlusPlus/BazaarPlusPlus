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

// Preserve existing log consumers' formats: Desktop uses snake_case, Android uses game member names.
public enum LiveCardSnapshotLogValues
{
    SnakeCase,
    MemberName,
}

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
