#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// Retains synchronous start feedback across the controller's immediate and per-frame refreshes.
/// A meaningful display-state transition owns clearing stale feedback; diagnostic prose is
/// deliberately excluded from that identity because it is not user-visible state.
/// </summary>
internal sealed class CurrentReplayRecordingStartFailureFeedback
{
    private CurrentReplayRecordingStatusCode? _statusCode;
    private SnapshotIdentity _snapshotIdentity;

    internal void ReportFailure(
        CurrentReplayRecordingStatusCode statusCode,
        CurrentReplayRecordingSnapshot snapshot
    )
    {
        _statusCode = statusCode;
        _snapshotIdentity = SnapshotIdentity.From(snapshot);
    }

    internal CurrentReplayRecordingStatusCode? Observe(CurrentReplayRecordingSnapshot snapshot)
    {
        if (!_statusCode.HasValue)
            return null;
        if (_snapshotIdentity == SnapshotIdentity.From(snapshot))
            return _statusCode;

        Clear();
        return null;
    }

    internal void Clear()
    {
        _statusCode = null;
        _snapshotIdentity = default;
    }

    private readonly record struct SnapshotIdentity(
        CurrentReplayRecordingPhase Phase,
        string? BattleId,
        string? RecordingId,
        string? FinalFilePath,
        bool Visible,
        bool CanStart,
        bool CanReveal,
        CurrentReplayRecordingStatusCode StatusCode
    )
    {
        internal static SnapshotIdentity From(CurrentReplayRecordingSnapshot snapshot) =>
            new(
                snapshot.Phase,
                snapshot.BattleId,
                snapshot.RecordingId,
                snapshot.FinalFilePath,
                snapshot.Visible,
                snapshot.CanStart,
                snapshot.CanReveal,
                snapshot.StatusCode
            );
    }
}
