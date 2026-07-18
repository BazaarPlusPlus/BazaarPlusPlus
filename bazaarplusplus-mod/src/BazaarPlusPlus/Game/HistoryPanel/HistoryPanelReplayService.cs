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
    private readonly Func<string?> _replayDirectoryPathAccessor;
    private readonly Func<string?> _pluginsDirectoryPathAccessor;
    private readonly Func<string?> _videoDirectoryPathAccessor;
    private readonly GhostBattleSyncService? _ghostSyncService;

    public HistoryPanelReplayService(
        Func<CombatReplayRuntime?> runtimeAccessor,
        Func<string?> replayDirectoryPathAccessor,
        Func<string?> pluginsDirectoryPathAccessor,
        Func<string?> videoDirectoryPathAccessor,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _runtimeAccessor =
            runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        _replayDirectoryPathAccessor =
            replayDirectoryPathAccessor
            ?? throw new ArgumentNullException(nameof(replayDirectoryPathAccessor));
        _pluginsDirectoryPathAccessor =
            pluginsDirectoryPathAccessor
            ?? throw new ArgumentNullException(nameof(pluginsDirectoryPathAccessor));
        _videoDirectoryPathAccessor =
            videoDirectoryPathAccessor
            ?? throw new ArgumentNullException(nameof(videoDirectoryPathAccessor));
        _ghostSyncService = ghostSyncService;
    }

    // Capture the Unity-owned dimensions/FPS on the UI thread, then resolve FFmpeg and its
    // actual-settings encoder profile in the background. Per-refresh gates only read warm state.
    public void PrewarmRecordingAvailability()
    {
        var pluginsDirectoryPath = _pluginsDirectoryPathAccessor();
        var videoDirectoryPath = _videoDirectoryPathAccessor();
        var hasSettings = ReplayVideoCaptureSettingsCache.TryCaptureCurrent(
            out var captureSettings
        );
        _ = Task.Run(() =>
        {
            var ffmpegExecutable = FfmpegLocator.Resolve(pluginsDirectoryPath);
            if (
                hasSettings
                && !string.IsNullOrWhiteSpace(ffmpegExecutable)
                && !string.IsNullOrWhiteSpace(videoDirectoryPath)
            )
            {
                FfmpegVideoEncoderSelector.Prewarm(
                    ffmpegExecutable,
                    videoDirectoryPath,
                    captureSettings.Width,
                    captureSettings.Height,
                    captureSettings.Fps
                );
            }
        });
    }

    // Recording is feasible only when the replay itself can run AND the shared recording gate
    // passes (async GPU readback + ffmpeg + video directory — the same gate the recorder
    // enforces at capture time). FfmpegLocator.Resolve hits the prewarmed cache here (no probe
    // on the UI thread).
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

        var gate = CombatReplayRecordingGate.Evaluate(
            _pluginsDirectoryPathAccessor(),
            _videoDirectoryPathAccessor()
        );
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

        var replayDirectoryPath = _replayDirectoryPathAccessor();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return HistoryPanelReplayAttemptResult.Failure(
                HistoryPanelText.CombatReplayDirectoryUnavailable(),
                HistoryPanelReplayReasonCode.ReplayDirectoryUnavailable
            );

        if (!battle.ReplayDownloaded)
        {
            if (_ghostSyncService == null)
                return HistoryPanelReplayAttemptResult.Failure(
                    HistoryPanelText.GhostReplayDownloadUnavailable(),
                    HistoryPanelReplayReasonCode.GhostDownloadUnavailable
                );

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
        }

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

        var ghostPayload = ghostPayloadResult.Payload;
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

        var replayDirectoryPath = _replayDirectoryPathAccessor();
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
