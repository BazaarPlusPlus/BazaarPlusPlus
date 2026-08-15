#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.Paths;
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
    private static int _recordingPrewarmKicked;
    private static volatile bool _recordingPrewarmCompleted;

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
        PrewarmRecordingOnce(services);

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

        ghost = GhostBattlePayloadReader.Normalize(ghost);
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

        // Native plugins are loaded synchronously on this Unity main-thread facade.
        if (!_recordingPrewarmCompleted)
        {
            reason = "Recording availability probe is still warming up; retry shortly.";
            return false;
        }

        var gate = CombatReplayRecordingGate.Evaluate(
            services.Paths.PluginsDirectoryPath,
            PathConstants.CombatReplayVideos(services.Paths.RequireDataRoot())
        );
        if (!gate.CanRecord)
        {
            reason = gate.Blocker switch
            {
                CombatReplayRecordingBlocker.UnsupportedPlatform =>
                    "Video recording is supported on macOS and Windows.",
                CombatReplayRecordingBlocker.NativeRecorderUnavailable =>
                    "Video recording is unavailable (the platform-native recorder could not be loaded).",
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
        PrewarmRecordingOnce(services);

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

    // Probe the platform backend the first time the host touches the facade. This facade is called
    // on Unity's main thread, which is mandatory for the first native-plugin load.
    private static void PrewarmRecordingOnce(IBppServices services)
    {
        if (Interlocked.Exchange(ref _recordingPrewarmKicked, 1) != 0)
            return;

        _ = services;
        switch (ReplayVideoBackendPolicy.Current)
        {
            case ReplayVideoBackend.MacNative:
                MacMetalVideoEncoder.TryGetAvailability(out _);
                break;
            case ReplayVideoBackend.WindowsNative:
                WindowsMediaFoundationVideoEncoder.TryGetAvailability(out _);
                break;
        }
        _recordingPrewarmCompleted = true;
    }
}
