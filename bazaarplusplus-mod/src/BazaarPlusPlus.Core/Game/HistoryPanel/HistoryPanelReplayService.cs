#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// History's replay over a host's saved-replay runtime and its recording availability: whether a
/// battle can replay or record, the replay itself (a Ghost battle downloads first), and the
/// cleanup of deleted runs' payloads. Both hosts compose it; only the two ports differ.
/// </summary>
public sealed class HistoryPanelReplayService : IHistoryPanelReplay
{
    private readonly Func<ISavedReplayRuntime?> _runtimeAccessor;
    private readonly IReplayRecordingAvailability _recording;
    private readonly string _replayDirectoryPath;
    private readonly GhostBattleSyncService? _ghostSyncService;
    private readonly Func<string?> _profileAccountId;

    public HistoryPanelReplayService(
        Func<ISavedReplayRuntime?> runtimeAccessor,
        IReplayRecordingAvailability recording,
        string replayDirectoryPath,
        Func<string?> profileAccountId,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _profileAccountId =
            profileAccountId ?? throw new ArgumentNullException(nameof(profileAccountId));
        _runtimeAccessor =
            runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        // Paths are startup-stable strings (no Func wrappers). Null coalesces to empty so
        // downstream IsNullOrWhiteSpace checks stay fail-open without ArgumentNullException.
        _replayDirectoryPath = replayDirectoryPath ?? string.Empty;
        _ghostSyncService = ghostSyncService;
    }

    public bool RecordingSupported => _recording.Supported;

    public void PrewarmRecordingAvailability() => _recording.Prewarm();

    // Recording is feasible only when the replay itself can run AND the host can record.
    public bool CanRecordReplay(
        HistoryBattleRecord? battle,
        out string reason,
        out HistoryPanelReplayReasonCode reasonCode
    )
    {
        if (!CanReplayBattle(battle, out reason))
        {
            reasonCode = HistoryPanelReplayReasonCode.ReplayUnavailable;
            return false;
        }

        return CanRecordLoadedReplay(out reason, out reasonCode);
    }

    public bool CanRecordLoadedReplay(out string reason) =>
        CanRecordLoadedReplay(out reason, out _);

    public bool CanRecordLoadedReplay(
        out string reason,
        out HistoryPanelReplayReasonCode reasonCode
    )
    {
        if (!_recording.CanRecord())
        {
            reason = HistoryPanelText.RecordingUnavailable();
            reasonCode = HistoryPanelReplayReasonCode.RecordingUnavailable;
            return false;
        }
        reason = string.Empty;
        reasonCode = HistoryPanelReplayReasonCode.RecordingAvailable;
        return true;
    }

    public bool CanReplayBattle(HistoryBattleRecord? battle, out string reason)
    {
        if (battle == null)
        {
            reason = HistoryPanelText.SelectBattleToReplay();
            return false;
        }

        var runtime = _runtimeAccessor();
        if (runtime == null)
        {
            reason = HistoryPanelText.CombatReplayRuntimeUnavailable();
            return false;
        }

        if (battle.Source == HistoryBattleSource.Ghost)
        {
            if (!runtime.CanReplaySavedCombats(out reason))
                return false;

            if (battle.Replay is ReplayAvailability.Saved or ReplayAvailability.Remote)
            {
                reason = string.Empty;
                return true;
            }

            reason = HistoryPanelDecisions.GhostReplayUnavailableReason(battle);
            return false;
        }

        if (battle.Replay != ReplayAvailability.Saved)
        {
            reason = HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId);
            return false;
        }

        return runtime.CanReplaySavedBattle(battle.BattleId, out reason);
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle)
    {
        return
            battle?.Source == HistoryBattleSource.Ghost
            && battle.Replay == ReplayAvailability.Remote
            ? HistoryPanelText.DownloadReplay()
            : HistoryPanelText.Replay();
    }

    public async Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        bool recordVideo,
        CancellationToken cancellationToken,
        Func<bool>? canCommit = null
    )
    {
        if (battle == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.SelectBattleToReplay(),
                HistoryPanelReplayReasonCode.ReplayUnavailable
            );

        if (!CanReplayBattle(battle, out var reason))
            return HistoryPanelReplayAttemptResult.Failure(
                reason,
                HistoryPanelReplayReasonCode.ReplayUnavailable
            );

        if (battle.Source == HistoryBattleSource.Ghost)
            return await ReplayGhostBattleAsync(battle, recordVideo, cancellationToken, canCommit);

        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayRuntimeUnavailable(),
                HistoryPanelReplayReasonCode.RuntimeUnavailable
            );

        CheckCommit(cancellationToken, canCommit);
        if (!runtime.ReplaySaved(battle.BattleId, recordVideo))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayRejectedForBattle(battle.BattleId),
                HistoryPanelReplayReasonCode.ReplayRejected
            );

        return HistoryPanelReplayAttemptResult.Success(
            HistoryPanelText.StartingReplayForBattle(battle.BattleId)
        );
    }

    private async Task<HistoryPanelReplayAttemptResult> ReplayGhostBattleAsync(
        HistoryBattleRecord battle,
        bool recordVideo,
        CancellationToken cancellationToken,
        Func<bool>? canCommit = null
    )
    {
        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayRuntimeUnavailable(),
                HistoryPanelReplayReasonCode.RuntimeUnavailable
            );

        var loaded = await LoadGhostReplayAsync(
            battle,
            _profileAccountId()?.Trim() ?? string.Empty,
            cancellationToken
        );
        if (loaded.Payload == null)
            return loaded.Failure;

        CheckCommit(cancellationToken, canCommit);
        if (
            !runtime.ReplayLoadedBattle(
                loaded.Payload.BattleManifest,
                loaded.Payload.ReplayPayload,
                recordVideo,
                CombatReplayPlaybackSource.ImportedGhost
            )
        )
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayRejectedForGhostBattle(battle.BattleId),
                HistoryPanelReplayReasonCode.ReplayRejected
            );

        return HistoryPanelReplayAttemptResult.Success(
            battle.Replay == ReplayAvailability.Saved
                ? HistoryPanelText.StartingReplayForBattle(battle.BattleId)
                : HistoryPanelText.DownloadedAndStartingReplay(battle.BattleId)
        );
    }

    // Downloads the Ghost replay when needed and loads its manifest and payload, checked against
    // the account that owns the Ghost row. Payload is null exactly when Failure says why.
    public async Task<GhostReplayLoad> LoadGhostReplayAsync(
        HistoryBattleRecord battle,
        string account,
        CancellationToken cancellationToken
    )
    {
        var replayDirectoryPath = _replayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return GhostReplayLoad.Failed(
                HistoryPanelText.CombatReplayDirectoryUnavailable(),
                HistoryPanelReplayReasonCode.ReplayDirectoryUnavailable
            );

        if (_ghostSyncService == null)
            return GhostReplayLoad.Failed(
                HistoryPanelText.GhostReplayDownloadUnavailable(),
                HistoryPanelReplayReasonCode.GhostDownloadUnavailable
            );

        // Always pass through the sync service so a local_ready row with a missing or corrupt
        // cache file can be validated and recovered instead of becoming a permanent dead row.
        var downloadResult = await _ghostSyncService.DownloadReplayAsync(
            battle.BattleId,
            replayDirectoryPath,
            cancellationToken
        );
        if (!downloadResult.Succeeded)
            return new GhostReplayLoad(
                null,
                HistoryPanelReplayAttemptResult.Failure(
                    HistoryPanelDecisions.GhostDownloadFailureMessage(
                        downloadResult.Availability,
                        downloadResult.Error,
                        downloadResult.ReasonCode
                    ),
                    downloadResult.ReasonCode,
                    downloadResult.Exception
                )
            );

        // The download already loaded (or extracted) and identity-checked the payload; reading the
        // file again would decompress and deserialize it a second time.
        var ghostPayload = downloadResult.Payload;
        if (ghostPayload?.BattleManifest == null)
            return GhostReplayLoad.Failed(
                HistoryPanelText.GhostManifestUnavailable(battle.BattleId),
                HistoryPanelReplayReasonCode.GhostManifestUnavailable
            );

        if (
            !GhostBattlePayloadReader.MatchesIdentity(
                ghostPayload,
                battle.BattleId,
                account,
                battle.OpponentAccountId ?? string.Empty
            )
        )
            return GhostReplayLoad.Failed(
                HistoryPanelText.GhostManifestUnavailable(battle.BattleId),
                HistoryPanelReplayReasonCode.GhostBattleMismatch
            );

        return new GhostReplayLoad(ghostPayload, default);
    }

    private static void CheckCommit(CancellationToken token, Func<bool>? canCommit)
    {
        token.ThrowIfCancellationRequested();
        if (canCommit?.Invoke() == false)
            throw new OperationCanceledException("History selection is no longer active.", token);
    }

    public Func<IDisposable?> PreparePayloadMaintenance()
    {
        var runtime = _runtimeAccessor();
        return () => runtime?.AcquirePayloadMaintenance();
    }

    public ReplayPayloadCleanupResult CleanupReplayPayloads(IReadOnlyList<string> battleIds)
    {
        if (battleIds.Count == 0)
            return new ReplayPayloadCleanupResult(0, null);

        var replayDirectoryPath = _replayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
        {
            return new ReplayPayloadCleanupResult(
                battleIds.Count,
                new InvalidOperationException("Replay directory unavailable.")
            );
        }

        try
        {
            var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
            var ghostPayloadStore = new GhostBattlePayloadStore(
                GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
            );
            return HistoryPanelReplayCleanup.Execute(
                battleIds,
                payloadStore.Delete,
                ghostPayloadStore.Delete
            );
        }
        catch (Exception ex)
        {
            return new ReplayPayloadCleanupResult(battleIds.Count, ex);
        }
    }
}
