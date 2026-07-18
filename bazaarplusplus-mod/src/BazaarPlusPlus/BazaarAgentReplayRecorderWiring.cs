#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus;

/// <summary>
/// Composition-root delegate bodies behind <see cref="IBazaarAgentReplayRecorder"/>. This is the
/// only place the BazaarAgent replay facade touches game types: decode the GhostBattlePayload
/// blob, run the full recording guard set (the union of HistoryPanelReplayService.CanRecordReplay
/// and the CombatReplayVideoRecorder OnPlaybackStarting guards — any miss there records nothing
/// silently), and call into the internal CombatReplayRuntime. The runtime accessor is lazy: the
/// facade is published before CombatReplayRuntime is attached. Main thread only.
/// </summary>
internal static class BazaarAgentReplayRecorderWiring
{
    private static int _ffmpegPrewarmKicked;
    private static volatile bool _ffmpegPrewarmCompleted;

    public static IBazaarAgentReplayRecorder Create(
        Func<CombatReplayRuntime?> runtimeAccessor,
        IBppServices services
    )
    {
        return new BazaarAgentReplayRecorder(
            tryStartRecord: (requestId, payloadBytes, expectedBattleId) =>
                TryStartRecord(
                    runtimeAccessor(),
                    services,
                    requestId,
                    payloadBytes,
                    expectedBattleId
                ),
            tryContinueReplay: () => TryContinueReplay(runtimeAccessor()),
            getReplayPhase: () => GetReplayPhase(runtimeAccessor(), services)
        );
    }

    private static BppReplayControlResult TryStartRecord(
        CombatReplayRuntime? runtime,
        IBppServices services,
        string requestId,
        byte[] payloadBytes,
        string? expectedBattleId
    )
    {
        PrewarmFfmpegOnce(services);

        if (runtime == null)
            return BppReplayControlResult.Unavailable("Combat replay runtime is unavailable.");

        // Guards before decode: this runs on the Unity main thread inside one controller tick,
        // and the gzip+msgpack decode of a multi-MB payload is the expensive part. A request
        // that is going to be rejected anyway (in-run, replay already active, recording
        // unavailable) must not pay it — especially for command bursts queued during a stall.
        if (!CanRecordNow(runtime, services, out var reason))
            return BppReplayControlResult.Rejected(reason);

        if (!GhostBattlePayloadCodec.TryDeserialize(payloadBytes, out var ghost, out var error))
            return BppReplayControlResult.Invalid($"Payload decode failed: {error}");

        var manifest = ghost!.BattleManifest;
        var payload = ghost.ReplayPayload;
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.BattleId))
            return BppReplayControlResult.Invalid("Payload is missing the battle manifest.");
        if (payload == null)
            return BppReplayControlResult.Invalid("Payload is missing the replay payload.");

        var battleId = manifest.BattleId;
        if (
            !string.IsNullOrWhiteSpace(ghost.BattleId)
            && !string.Equals(ghost.BattleId, battleId, StringComparison.Ordinal)
        )
        {
            return BppReplayControlResult.Invalid(
                $"Payload battleId '{ghost.BattleId}' does not match manifest battleId '{battleId}'."
            );
        }

        if (
            !string.IsNullOrWhiteSpace(expectedBattleId)
            && !string.Equals(expectedBattleId, battleId, StringComparison.Ordinal)
        )
        {
            return BppReplayControlResult.Invalid(
                $"Request battleId '{expectedBattleId}' does not match payload battleId '{battleId}'."
            );
        }

        if (!runtime.ReplayImportedBattle(manifest, payload, recordVideo: true))
            return BppReplayControlResult.Rejected("Replay runtime rejected the imported battle.");

        BppLog.DebugEvent(
            CombatReplayLogEvents.ExternalRecordAccepted,
            () =>
                [
                    CombatReplayLogEvents.ExternalRecordAcceptedRequestId.Bind(requestId),
                    CombatReplayLogEvents.ExternalRecordAcceptedBattleId.Bind(battleId),
                    CombatReplayLogEvents.ExternalRecordAcceptedSource.Bind(
                        ReplayExternalRecordSource.Agent
                    ),
                ]
        );
        return BppReplayControlResult.Accepted(battleId);
    }

    // The recorder's OnPlaybackStarting bails out silently when the recording gate fails (the
    // replay still plays, nothing is recorded) — so a record request must be rejected up front
    // instead of returning "accepted" for a session that will never produce an mp4.
    private static bool CanRecordNow(
        CombatReplayRuntime runtime,
        IBppServices services,
        out string reason
    )
    {
        if (!runtime.CanReplaySavedCombats(out reason))
            return false;

        // FfmpegLocator.Resolve probes (~2s, with WaitForExit calls) on its first process-wide
        // call. Never pay that on the Unity main thread: until the off-thread prewarm has
        // finished, answer with a retryable rejection instead of freezing the game.
        if (!_ffmpegPrewarmCompleted)
        {
            reason = "Recording availability probe is still warming up; retry shortly.";
            return false;
        }

        var gate = CombatReplayRecordingGate.Evaluate(
            services.Paths.PluginsDirectoryPath,
            services.Paths.CombatReplayVideoDirectoryPath
        );
        if (!gate.CanRecord)
        {
            reason = gate.Blocker switch
            {
                CombatReplayRecordingBlocker.NoAsyncGpuReadback =>
                    "Video recording is unavailable on this device (no async GPU readback).",
                CombatReplayRecordingBlocker.FfmpegUnavailable =>
                    "Video recording is unavailable (FFmpeg could not be resolved).",
                _ => "Video recording is unavailable (video directory is not configured).",
            };
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static BppReplayControlResult TryContinueReplay(CombatReplayRuntime? runtime)
    {
        if (runtime == null)
            return BppReplayControlResult.Unavailable("Combat replay runtime is unavailable.");

        if (!runtime.TryContinueReplay(out var reason))
            return BppReplayControlResult.Rejected(reason);

        return BppReplayControlResult.Accepted(runtime.ActiveBattleId);
    }

    private static BppReplayPhaseSnapshot GetReplayPhase(
        CombatReplayRuntime? runtime,
        IBppServices services
    )
    {
        PrewarmFfmpegOnce(services);

        if (runtime == null)
            return new BppReplayPhaseSnapshot(BppReplayPhase.None, null);

        if (runtime.IsReplayStartInProgress)
            return new BppReplayPhaseSnapshot(BppReplayPhase.Starting, runtime.ActiveBattleId);

        if (AppState.CurrentState is ReplayState replay)
        {
            var phase = replay.IsReplaying
                ? BppReplayPhase.Playing
                : BppReplayPhase.FinishedAwaitingContinue;
            return new BppReplayPhaseSnapshot(phase, runtime.ActiveBattleId);
        }

        return new BppReplayPhaseSnapshot(BppReplayPhase.None, null);
    }

    // Resolve FFmpeg and the actual-dimensions encoder profile off-thread the first time the host
    // touches the facade, so CanRecordNow and the first accepted recording only read warm caches.
    private static void PrewarmFfmpegOnce(IBppServices services)
    {
        if (Interlocked.Exchange(ref _ffmpegPrewarmKicked, 1) != 0)
            return;

        var pluginsDirectoryPath = services.Paths.PluginsDirectoryPath;
        var videoDirectoryPath = services.Paths.CombatReplayVideoDirectoryPath;
        var hasSettings = ReplayVideoCaptureSettingsCache.TryGet(out var captureSettings);
        _ = Task.Run(() =>
        {
            try
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
            }
            finally
            {
                _ffmpegPrewarmCompleted = true;
            }
        });
    }
}
