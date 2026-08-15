#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Video;

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum CurrentReplayRecordingPhase
{
    Unavailable,
    AwaitingBattlePersistence,
    Preparing,
    Ready,
    Armed,
    Recording,
    Finalizing,
    Succeeded,
    Degraded,
    Failed,
}

internal readonly record struct CurrentReplayRecordingSnapshot(
    CurrentReplayRecordingPhase Phase,
    string? BattleId,
    string? RecordingId,
    string? FinalFilePath,
    string? Reason,
    bool Visible,
    bool CanStart,
    bool CanReveal
);

internal sealed class CurrentReplayRecordingState
{
    private string? _battleId;
    private string? _recordingId;
    private string? _finalFilePath;
    private string? _reason;
    private bool _replaySourceReady;
    private bool _replayStateActive;
    private bool _availabilityReady;
    private bool _nativeReplayStarted;

    internal CurrentReplayRecordingPhase Phase { get; private set; } =
        CurrentReplayRecordingPhase.Unavailable;

    internal string? BattleId => _battleId;
    internal string? RecordingId => _recordingId;
    internal bool NativeReplayStarted => _nativeReplayStarted;
    internal bool HasActiveSession =>
        Phase
            is CurrentReplayRecordingPhase.Armed
                or CurrentReplayRecordingPhase.Recording
                or CurrentReplayRecordingPhase.Finalizing;

    internal void LatchBattle(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        _battleId = battleId;
        _recordingId = null;
        _finalFilePath = null;
        _reason = null;
        _replaySourceReady = false;
        _availabilityReady = false;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.AwaitingBattlePersistence;
    }

    internal void EnterReplayState()
    {
        _replayStateActive = true;
        RefreshReadyPhase();
    }

    internal void MarkBattlePersistence(string battleId, bool succeeded, string? reason)
    {
        if (!MatchesBattle(battleId) || HasActiveSession)
            return;

        _replaySourceReady = succeeded;
        _reason = succeeded ? null : reason;
        Phase = succeeded
            ? CurrentReplayRecordingPhase.Preparing
            : CurrentReplayRecordingPhase.Failed;
        RefreshReadyPhase();
    }

    internal void MarkCurrentNativeReady(string battleId)
    {
        if (!MatchesBattle(battleId) || HasActiveSession)
            return;

        _replaySourceReady = true;
        _reason = null;
        Phase = CurrentReplayRecordingPhase.Preparing;
        RefreshReadyPhase();
    }

    internal void SetAvailability(bool ready, string? reason)
    {
        if (
            _battleId == null
            || HasActiveSession
            || Phase
                is CurrentReplayRecordingPhase.Succeeded
                    or CurrentReplayRecordingPhase.Degraded
        )
            return;

        _availabilityReady = ready;
        if (!ready)
        {
            _reason = reason;
            if (_replaySourceReady && Phase != CurrentReplayRecordingPhase.Failed)
                Phase = CurrentReplayRecordingPhase.Preparing;
            return;
        }

        if (Phase == CurrentReplayRecordingPhase.Failed)
            return;

        _reason = null;
        RefreshReadyPhase();
    }

    internal bool TryArm(string recordingId)
    {
        if (
            string.IsNullOrWhiteSpace(recordingId)
            || !_replayStateActive
            || !_replaySourceReady
            || !_availabilityReady
            || Phase
                is not (CurrentReplayRecordingPhase.Ready or CurrentReplayRecordingPhase.Failed)
        )
        {
            return false;
        }

        _recordingId = recordingId;
        _finalFilePath = null;
        _reason = null;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.Armed;
        return true;
    }

    internal bool MarkNativeReplayStarted()
    {
        if (Phase != CurrentReplayRecordingPhase.Armed || _recordingId == null)
            return false;

        _nativeReplayStarted = true;
        return true;
    }

    internal void MarkRecordingStarted(string recordingId, string battleId)
    {
        if (!MatchesSession(recordingId, battleId) || !_nativeReplayStarted)
            return;

        Phase = CurrentReplayRecordingPhase.Recording;
        _reason = null;
    }

    internal void RollbackArm(string recordingId, string reason)
    {
        if (!string.Equals(_recordingId, recordingId, StringComparison.Ordinal))
            return;

        _recordingId = null;
        _nativeReplayStarted = false;
        _reason = reason;
        Phase =
            _replaySourceReady && _availabilityReady
                ? CurrentReplayRecordingPhase.Ready
                : CurrentReplayRecordingPhase.Failed;
    }

    internal void MarkReplayEnded(string? reason = null)
    {
        if (!_nativeReplayStarted)
            return;

        _nativeReplayStarted = false;
        if (Phase == CurrentReplayRecordingPhase.Recording)
        {
            Phase = CurrentReplayRecordingPhase.Finalizing;
            _reason = reason;
            return;
        }

        if (Phase == CurrentReplayRecordingPhase.Armed)
        {
            Phase = CurrentReplayRecordingPhase.Failed;
            _reason = reason ?? "Video recording failed to start.";
        }
    }

    internal void ApplyCompletion(CombatReplayVideoRecordingCompleted completion)
    {
        if (
            completion.Source != CombatReplayPlaybackSource.CurrentNative
            || !MatchesSession(completion.RecordingId, completion.BattleId)
        )
        {
            return;
        }

        _finalFilePath = completion.ArtifactUsable ? completion.FinalFilePath : null;
        _reason = completion.Reason;
        _nativeReplayStarted = false;
        if (!completion.ArtifactUsable)
        {
            Phase = CurrentReplayRecordingPhase.Failed;
            return;
        }

        Phase =
            completion.MetadataStatus == ReplayVideoMetadataStatus.Complete
                ? completion.ReasonCode == ReplayVideoRecordingReasonCode.Completed
                    ? CurrentReplayRecordingPhase.Succeeded
                    : CurrentReplayRecordingPhase.Degraded
                : CurrentReplayRecordingPhase.Degraded;
    }

    internal void LeaveReplayState()
    {
        Reset();
    }

    internal CurrentReplayRecordingSnapshot Snapshot()
    {
        var visible = _replayStateActive && _battleId != null;
        var canReveal =
            visible
            && !string.IsNullOrWhiteSpace(_finalFilePath)
            && Phase
                is CurrentReplayRecordingPhase.Succeeded
                    or CurrentReplayRecordingPhase.Degraded;
        var canStart =
            visible
            && _replaySourceReady
            && _availabilityReady
            && Phase is CurrentReplayRecordingPhase.Ready or CurrentReplayRecordingPhase.Failed;
        return new CurrentReplayRecordingSnapshot(
            Phase,
            _battleId,
            _recordingId,
            _finalFilePath,
            _reason,
            visible,
            canStart,
            canReveal
        );
    }

    private bool MatchesBattle(string battleId) =>
        string.Equals(_battleId, battleId, StringComparison.Ordinal);

    private bool MatchesSession(string recordingId, string battleId) =>
        MatchesBattle(battleId)
        && string.Equals(_recordingId, recordingId, StringComparison.Ordinal);

    private void RefreshReadyPhase()
    {
        if (_battleId == null || HasActiveSession || !_replaySourceReady)
            return;

        Phase =
            _replayStateActive && _availabilityReady
                ? CurrentReplayRecordingPhase.Ready
                : CurrentReplayRecordingPhase.Preparing;
    }

    private void Reset()
    {
        _battleId = null;
        _recordingId = null;
        _finalFilePath = null;
        _reason = null;
        _replaySourceReady = false;
        _replayStateActive = false;
        _availabilityReady = false;
        _nativeReplayStarted = false;
        Phase = CurrentReplayRecordingPhase.Unavailable;
    }
}

internal static class ReplayRecordingButtonSnapshotPolicy
{
    internal static CurrentReplayRecordingSnapshot OrdinaryManagedReplay(
        string battleId,
        bool recorderReady,
        bool replayReady,
        string? unavailableReason
    ) =>
        new(
            recorderReady && replayReady
                ? CurrentReplayRecordingPhase.Ready
                : CurrentReplayRecordingPhase.Preparing,
            battleId,
            RecordingId: null,
            FinalFilePath: null,
            Reason: recorderReady && replayReady ? null : unavailableReason,
            Visible: true,
            CanStart: recorderReady && replayReady,
            CanReveal: false
        );

    internal static CurrentReplayRecordingSnapshot Resolve(
        CurrentReplayRecordingSnapshot managedSnapshot,
        CurrentReplayRecordingSnapshot currentNativeSnapshot
    ) => managedSnapshot.Visible ? managedSnapshot : currentNativeSnapshot;
}
