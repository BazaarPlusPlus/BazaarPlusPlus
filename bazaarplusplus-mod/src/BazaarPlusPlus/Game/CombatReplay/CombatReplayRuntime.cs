#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Warmup;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
{
    private IBppServices? _services;
    private RunLifecycleModule? _runLifecycle;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
    private ReplayPersistenceOrchestrator? _persistence;
    private ReplayPlaybackPublisher? _playbackPublisher;
    private OpponentPortraitController? _portraitController;

    private bool _returnToMenuAfterReplay;
    private bool _bootstrappedReplayActive;

    // Joint progress of a saved-replay playback session. "Start in progress" and "playback
    // session active" overlap by design (the session is live for the whole start), so the
    // states encode their combinations rather than one-hot flags:
    //   Idle                -> no session, no start in flight
    //   StartInProgress     -> StartReplayAsync running, playback session live
    //   SavedPlaybackActive -> start finished, playback session live until ReplayState exits
    //   StartFailureCleanup -> playback session cleared but StartReplayAsync is still
    //                          unwinding (failure publish + bootstrap rollback)
    private enum SavedReplayProgress
    {
        Idle,
        StartInProgress,
        SavedPlaybackActive,
        StartFailureCleanup,
    }

    private SavedReplayProgress _savedReplayProgress;

    // Latched while a replay exit is in flight but ReplayState has not actually been left yet
    // (the bootstrapped exit path keeps CurrentState == ReplayState for the whole async
    // menu-return). Guards against a second Exit(): after the first exit cleared the
    // bootstrapped flags, a duplicate Exit() would run the original ReplayState.Exit() body
    // (whose own _exitRequested was never set on the rerouted path) and dispatch the dead
    // replay's despawn GameSim into the live state machine mid transition.
    //
    // The suppression is TIME-BOUNDED: ReturnToMainMenu awaits a network call internally and
    // can silently fail, leaving the game parked in ReplayState forever. Past the window, a
    // fresh Exit() (native click or continue endpoint) is allowed through again as the escape
    // hatch — running the original Exit body is the lesser evil versus a permanently dead
    // continue button.
    private const float ReplayExitSuppressionWindowSeconds = 15f;
    private bool _replayExitInProgress;
    private float _replayExitRequestedAtRealtime;

    private bool IsReplayExitSuppressionActive =>
        _replayExitInProgress
        && Time.realtimeSinceStartup - _replayExitRequestedAtRealtime
            < ReplayExitSuppressionWindowSeconds;

    private void LatchReplayExitInProgress()
    {
        _replayExitInProgress = true;
        _replayExitRequestedAtRealtime = Time.realtimeSinceStartup;
    }

    public static CombatReplayRuntime? Instance { get; private set; }

    // Sourced from the playback session (BeginSession sets it for both the local-saved and the
    // imported-ghost path); the controller only learns battle ids on the local-saved path.
    public string? ActiveBattleId => _playbackPublisher?.ActiveSessionBattleId;

    public bool IsReplayPlaybackActive =>
        IsSavedReplayPlaybackActive || AppState.CurrentState is ReplayState;

    public bool IsSavedReplayPlaybackActive =>
        _savedReplayProgress
            is SavedReplayProgress.StartInProgress
                or SavedReplayProgress.SavedPlaybackActive;

    public bool IsReplayStartInProgress =>
        _savedReplayProgress
            is SavedReplayProgress.StartInProgress
                or SavedReplayProgress.StartFailureCleanup;

    public bool HasPendingPersistence => _persistence?.HasPendingPersistence == true;

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize(
        IBppServices services,
        RunLifecycleModule runLifecycle,
        IPvpBattleCatalog battleCatalog
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));

        _persistence = new ReplayPersistenceOrchestrator(_services, battleCatalog);
        _playbackPublisher = new ReplayPlaybackPublisher(_services);
        _portraitController = new OpponentPortraitController(Destroy);
        _captureService = new CombatReplayCaptureService();
        _loader = new CombatReplayLoader();
        _controller = new CombatReplayController(
            _persistence.Catalog,
            _persistence.PayloadStore,
            _loader
        );

        Events.StateChanged.AddListener(OnStateChanged, this);
    }

    private void Update()
    {
        _persistence?.DrainPendingResults();

        // The exit-in-progress latch clears itself once ReplayState is actually gone; this is
        // the only reliable signal on the bootstrapped exit path, where the state transition
        // happens via RunManager.ReturnToMainMenu without a normal ReplayState exit event.
        if (_replayExitInProgress && AppState.CurrentState is not ReplayState)
            _replayExitInProgress = false;
    }

    private void OnDestroy()
    {
        _persistence?.Dispose();

        if (Instance == this)
            Instance = null;

        Events.StateChanged.RemoveListener(OnStateChanged);
    }

    public IReadOnlyList<PvpBattleManifest> ListRecentBattles()
    {
        return _controller?.ListRecentBattles() ?? Array.Empty<PvpBattleManifest>();
    }

    public PvpBattleManifest? GetLatestBattle()
    {
        return _controller?.GetLatestBattle();
    }

    public bool CanReplaySavedCombats(out string reason)
    {
        if (IsReplayStartInProgress)
        {
            reason = "A saved replay is already starting.";
            return false;
        }

        if (_services!.RunContext.IsInGameRun)
        {
            reason =
                "Saved replay playback is only available while you are outside an active gameplay session.";
            return false;
        }

        if (AppState.CurrentState is ReplayState)
        {
            reason = "A replay is already in progress.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanReplaySavedBattle(string battleId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            reason = "Select a saved battle to replay.";
            return false;
        }

        if (_controller == null)
        {
            reason = "Combat replay runtime is unavailable.";
            return false;
        }

        if (!CanReplaySavedCombats(out reason))
            return false;

        if (!_controller.HasSavedReplay(battleId))
        {
            reason = "Replay payload for the selected battle is unavailable.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public void ObserveMessage(BazaarGameShared.Infra.Messages.INetMessage message)
    {
        if (_captureService == null || _persistence == null)
            return;

        try
        {
            var artifact = _captureService.Accept(
                message,
                _services!.RunContext.CurrentServerRunId
            );
            if (artifact == null)
                return;

            _persistence.Enqueue(artifact.Payload, artifact.Manifest);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to capture combat replay: {ex}");
        }
    }

    public bool ReplayLatest()
    {
        var latest = _controller?.GetLatestBattle();
        if (latest == null)
            return false;

        return ReplaySaved(latest.BattleId, recordVideo: false);
    }

    public bool ReplaySaved(string battleId, bool recordVideo)
    {
        if (!CanReplaySavedBattle(battleId, out var reason))
        {
            BppLog.Warn("CombatReplayRuntime", $"Rejected saved replay request: {reason}");
            return false;
        }

        var controller = _controller;
        if (controller == null)
            return false;

        var manifest = controller.LoadBattle(battleId);
        if (manifest == null)
            return false;

        var payload = controller.LoadPayload(manifest);
        if (payload == null)
            return false;

        var sequence = controller.LoadReplay(payload);
        PlaybackUiState.InitializedBoardUiControllers.Clear();
        _savedReplayProgress = SavedReplayProgress.SavedPlaybackActive;
        _ = StartReplayAsync(
            manifest,
            sequence,
            battleId,
            CombatReplayPlaybackSource.LocalSaved,
            recordVideo
        );
        return true;
    }

    public bool ReplayImportedBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        bool recordVideo
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        if (!CanReplaySavedCombats(out var reason))
        {
            BppLog.Warn("CombatReplayRuntime", $"Rejected imported replay request: {reason}");
            return false;
        }

        var loader = _loader;
        if (loader == null)
            return false;

        var sequence = loader.Load(payload);
        PlaybackUiState.InitializedBoardUiControllers.Clear();
        _savedReplayProgress = SavedReplayProgress.SavedPlaybackActive;
        _ = StartReplayAsync(
            manifest,
            sequence,
            manifest.BattleId,
            CombatReplayPlaybackSource.ImportedGhost,
            recordVideo
        );
        return true;
    }

    /// <summary>
    /// Drives the replay "continue" button programmatically: validates that playback has finished
    /// and is waiting on the button, then runs the same chain a real click does
    /// (BoardManager.OnBoardRecapReplayButtonsContinueClicked: LevelUp recap cleanup, then
    /// <c>ReplayState.Exit()</c>). This is the only programmatic path allowed to exit ReplayState —
    /// finalizing any in-flight video recording depends on it.
    /// </summary>
    public bool TryContinueReplay(out string reason)
    {
        if (AppState.CurrentState is not ReplayState replay)
        {
            reason = "No replay is active.";
            return false;
        }

        if (IsReplayStartInProgress)
        {
            reason = "Replay playback is still starting.";
            return false;
        }

        if (replay.IsReplaying)
        {
            reason = "Replay playback has not finished yet.";
            return false;
        }

        if (IsReplayExitSuppressionActive)
        {
            reason = "Replay exit is already in progress.";
            return false;
        }

        // Mirror the native continue click: clear the LevelUp recap overlay first
        // (BoardManager.OnBoardRecapReplayButtonsContinueClicked guards on ERunState.LevelUp),
        // then Exit(). For bootstrapped saved replays the Exit() prefix patch reroutes into
        // TryExitBootstrappedSavedReplayToMenu, which publishes the recorder's "ended" signal.
        if (Data.CurrentState?.StateName == BazaarGameShared.Domain.Runs.ERunState.LevelUp)
            Singleton<BoardManager>.Instance?.ExitRecapReplayState();

        replay.Exit();
        LatchReplayExitInProgress();
        reason = string.Empty;
        return true;
    }

    private async Task StartReplayAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId,
        CombatReplayPlaybackSource source,
        bool recordVideo
    )
    {
        var attemptedBootstrapFromLobby = false;
        _savedReplayProgress = SavedReplayProgress.StartInProgress;
        _playbackPublisher!.BeginSession(battleId, manifest, source, recordVideo);
        try
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _portraitController!.Cleanup();
            _portraitController.ApplySelectedHeroOverride(manifest);
            Data.ResetRunData();
            _runLifecycle!.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !ReplayBootstrap.IsBootstrapReady();
            var bootstrappedFromLobby = await ReplayBootstrap.EnsureBootstrapReadyAsync();
            _returnToMenuAfterReplay = bootstrappedFromLobby;
            var bootstrapContext = ReplayBootstrap.ResolveDependencies();
            OpponentPortraitController.EnsureOpponentIdentity(manifest, sequence.SpawnMessage);
            await _portraitController.EnsureTemporaryOpponentPortraitAsync(manifest);
            await ReplayBootstrap.InjectSavedReplayAsync(
                bootstrapContext,
                manifest,
                sequence,
                battleId,
                _playbackPublisher.PublishStarting
            );
            _bootstrappedReplayActive = bootstrappedFromLobby;
            BppLog.Info("CombatReplayRuntime", $"Started replay for saved combat {battleId}");
        }
        catch (Exception ex)
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _savedReplayProgress = SavedReplayProgress.StartFailureCleanup;
            _portraitController!.Cleanup();
            _portraitController.RestoreSelectedHeroOverride();
            BppLog.Error("CombatReplayRuntime", $"Failed to start replay {battleId}: {ex}");
            // Unconditional: PublishEnded only publishes the event when "starting" was
            // published, but it must always clear the session (battle id) for a failed start.
            _playbackPublisher!.PublishEnded("start-failed", failed: true);
            if (attemptedBootstrapFromLobby)
                await ReplayBootstrap.RollbackBootstrapAsync();
        }
        finally
        {
            _savedReplayProgress = _savedReplayProgress switch
            {
                SavedReplayProgress.StartInProgress => SavedReplayProgress.SavedPlaybackActive,
                SavedReplayProgress.StartFailureCleanup => SavedReplayProgress.Idle,
                _ => _savedReplayProgress,
            };
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (data == null)
            return;

        if (data.PreviousState is not ReplayState || data.CurrentState is ReplayState)
            return;

        _portraitController?.RestoreSelectedHeroOverride();
        _savedReplayProgress = _savedReplayProgress switch
        {
            SavedReplayProgress.StartInProgress => SavedReplayProgress.StartFailureCleanup,
            SavedReplayProgress.SavedPlaybackActive => SavedReplayProgress.Idle,
            _ => _savedReplayProgress,
        };
        _playbackPublisher?.PublishEnded("state-exit", failed: false);
        _portraitController?.Cleanup();
        PlaybackUiState.InitializedBoardUiControllers.Clear();

        if (!_returnToMenuAfterReplay || !_bootstrappedReplayActive)
            return;

        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;

        try
        {
            BppLog.Info(
                "CombatReplayRuntime",
                "Returning to main menu after bootstrapped replay exit."
            );
            Services.Get<RunManager>()?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayRuntime",
                $"Failed to return to main menu after replay: {ex}"
            );
        }
    }

    internal static bool TryExitBootstrappedSavedReplayToMenu()
    {
        var instance = Instance;
        if (instance == null)
            return false;

        // A replay exit is already in flight (the bootstrapped flags were cleared, but the
        // async menu-return has not left ReplayState yet). Report "handled" so the Exit()
        // prefix patch suppresses the original body — running it now would dispatch the dead
        // replay's despawn GameSim into the live state machine mid transition. Time-bounded:
        // see ReplayExitSuppressionWindowSeconds.
        if (instance.IsReplayExitSuppressionActive && AppState.CurrentState is ReplayState)
            return true;

        if (!instance.IsSavedReplayPlaybackActive || !instance._bootstrappedReplayActive)
            return false;

        instance.ExitBootstrappedSavedReplayToMenu();
        return true;
    }

    private void ExitBootstrappedSavedReplayToMenu()
    {
        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        _savedReplayProgress = SavedReplayProgress.Idle;
        // Covers the native continue-click path too (it never goes through TryContinueReplay);
        // Update() clears the latch once ReplayState is actually gone.
        LatchReplayExitInProgress();
        _portraitController?.RestoreSelectedHeroOverride();
        // Bootstrapped saved replays exit through this manual path (the state-exit patch
        // intercepts the normal transition), so OnStateChanged's PublishEnded never fires for
        // them. Emit it here too, otherwise the video recorder never gets the "ended" signal and
        // leaves ffmpeg running on a never-finalized file (no moov atom -> unplayable MP4).
        _playbackPublisher?.PublishEnded("saved-replay-exit", failed: false);
        _portraitController?.Cleanup();
        PlaybackUiState.InitializedBoardUiControllers.Clear();

        try
        {
            BppLog.Info("CombatReplayRuntime", "Returning to main menu after saved replay exit.");
            Services.Get<RunManager>()?.ReturnToMainMenu();
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to exit saved replay: {ex}");
        }
    }

    // Patches/Combat/CombatReplayVisualPatches.cs calls this static facade — keep the surface.
    public static void HideEncounterPickerOverlays() =>
        HealthBarBinder.HideEncounterPickerOverlays();
}
