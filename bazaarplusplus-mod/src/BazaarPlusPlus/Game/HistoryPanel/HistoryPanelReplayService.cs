#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelReplayService
{
    private readonly Func<CombatReplayRuntime?> _runtimeAccessor;
    private readonly string _replayDirectoryPath;
    private readonly string _pluginsDirectoryPath;
    private readonly string _videoDirectoryPath;
    private readonly GhostBattleSyncService? _ghostSyncService;

    public HistoryPanelReplayService(
        Func<CombatReplayRuntime?> runtimeAccessor,
        string replayDirectoryPath,
        string pluginsDirectoryPath,
        string videoDirectoryPath,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _runtimeAccessor =
            runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        // Paths are startup-stable strings (no Func wrappers). Null coalesces to empty so
        // downstream IsNullOrWhiteSpace checks stay fail-open without ArgumentNullException.
        _replayDirectoryPath = replayDirectoryPath ?? string.Empty;
        _pluginsDirectoryPath = pluginsDirectoryPath ?? string.Empty;
        _videoDirectoryPath = videoDirectoryPath ?? string.Empty;
        _ghostSyncService = ghostSyncService;
    }

    // Capture Unity-owned dimensions/FPS and load the desktop native plugin on the UI thread.
    public void PrewarmRecordingAvailability()
    {
        ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out _);
        var backend = ReplayVideoBackendPolicy.Current;
        if (backend == ReplayVideoBackend.MacNative)
        {
            MacMetalVideoEncoder.TryGetAvailability(out _);
            return;
        }
        if (backend == ReplayVideoBackend.WindowsNative)
            WindowsMediaFoundationVideoEncoder.TryGetAvailability(out _);
    }

    // Recording is feasible only when the replay itself can run AND the shared recording gate
    // passes (a native desktop encoder plus a video directory). The backend probe is warm here.
    public bool CanRecordReplay(HistoryBattleRecord? battle, out string reason)
    {
        return CanRecordReplay(battle, out reason, out _);
    }

    internal bool CanRecordReplay(
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

        var gate = CombatReplayRecordingGate.Evaluate(_pluginsDirectoryPath, _videoDirectoryPath);
        if (!gate.CanRecord)
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

            if (battle.ReplayDownloaded || battle.ReplayAvailable)
            {
                reason = string.Empty;
                return true;
            }

            reason = HistoryPanelText.GhostReplayPayloadUnavailable();
            return false;
        }

        return runtime.CanReplaySavedBattle(battle.BattleId, out reason);
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle)
    {
        return
            battle?.Source == HistoryBattleSource.Ghost
            && !battle.ReplayDownloaded
            && battle.ReplayAvailable
            ? HistoryPanelText.DownloadReplay()
            : HistoryPanelText.Replay();
    }

    public async Task<HistoryPanelReplayAttemptResult> ReplayBattleAsync(
        HistoryBattleRecord? battle,
        bool recordVideo,
        CancellationToken cancellationToken
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
            return await ReplayGhostBattleAsync(battle, recordVideo, cancellationToken);

        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayRuntimeUnavailable(),
                HistoryPanelReplayReasonCode.RuntimeUnavailable
            );

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
        CancellationToken cancellationToken
    )
    {
        var runtime = _runtimeAccessor();
        if (runtime == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayRuntimeUnavailable(),
                HistoryPanelReplayReasonCode.RuntimeUnavailable
            );

        var replayDirectoryPath = _replayDirectoryPath;
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayDirectoryUnavailable(),
                HistoryPanelReplayReasonCode.ReplayDirectoryUnavailable
            );

        if (_ghostSyncService == null)
            return HistoryPanelReplayAttemptResult.Failure(
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
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.FailedToDownloadGhostReplay(
                    downloadResult.Error ?? HistoryPanelText.Unknown()
                ),
                downloadResult.ReasonCode,
                downloadResult.Exception
            );

        var ghostPayloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        var ghostPayloadResult = ghostPayloadStore.LoadDetailed(battle.BattleId);
        if (ghostPayloadResult.Status == FileBackedPayloadLoadStatus.Invalid)
        {
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId),
                HistoryPanelReplayReasonCode.ReplayPayloadInvalid
            );
        }
        if (ghostPayloadResult.Status == FileBackedPayloadLoadStatus.Unreadable)
        {
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId),
                HistoryPanelReplayReasonCode.ReplayPayloadUnreadable,
                ghostPayloadResult.Exception
            );
        }

        var ghostPayload = GhostBattlePayloadReader.Normalize(ghostPayloadResult.Payload);
        var manifest = ghostPayload?.BattleManifest;
        if (manifest == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.GhostManifestUnavailable(battle.BattleId),
                HistoryPanelReplayReasonCode.GhostManifestUnavailable
            );

        var payload = ghostPayload?.ReplayPayload;
        if (payload == null)
        {
            var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
            var payloadResult = payloadStore.LoadDetailed(battle.BattleId);
            if (payloadResult.Status == FileBackedPayloadLoadStatus.Invalid)
            {
                return HistoryPanelReplayAttemptResult.Failure(
                    HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId),
                    HistoryPanelReplayReasonCode.ReplayPayloadInvalid
                );
            }
            if (payloadResult.Status == FileBackedPayloadLoadStatus.Unreadable)
            {
                return HistoryPanelReplayAttemptResult.Failure(
                    HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId),
                    HistoryPanelReplayReasonCode.ReplayPayloadUnreadable,
                    payloadResult.Exception
                );
            }
            payload = payloadResult.Payload;
        }
        if (payload == null)
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayPayloadUnavailable(battle.BattleId),
                HistoryPanelReplayReasonCode.ReplayPayloadMissing
            );

        if (!runtime.ReplayImportedBattle(manifest, payload, recordVideo))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.ReplayRejectedForGhostBattle(battle.BattleId),
                HistoryPanelReplayReasonCode.ReplayRejected
            );

        return HistoryPanelReplayAttemptResult.Success(
            battle.ReplayDownloaded
                ? HistoryPanelText.StartingReplayForBattle(battle.BattleId)
                : HistoryPanelText.DownloadedAndStartingReplay(battle.BattleId)
        );
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

internal readonly struct HistoryPanelReplayAttemptResult
{
    private HistoryPanelReplayAttemptResult(
        bool succeeded,
        string statusMessage,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Succeeded = succeeded;
        StatusMessage = statusMessage;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public string StatusMessage { get; }
    public HistoryPanelReplayReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static HistoryPanelReplayAttemptResult Success(string statusMessage) =>
        new(true, statusMessage, HistoryPanelReplayReasonCode.Completed, null);

    public static HistoryPanelReplayAttemptResult Failure(
        string statusMessage,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, statusMessage, reasonCode, exception);
}
