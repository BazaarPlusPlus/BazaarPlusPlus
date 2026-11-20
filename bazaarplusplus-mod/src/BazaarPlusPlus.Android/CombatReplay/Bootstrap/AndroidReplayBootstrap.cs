using BazaarPlusPlus.Android.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2Cpp;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

internal static class AndroidReplayBootstrap
{
    private static readonly TimeSpan SceneTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(20);

    public static bool IsBootstrapReady()
    {
        var boardManager = BoardManager.Instance;
        var gameServiceManager = GameServiceManager.Instance;
        return SceneLoader.IsSceneLoaded(SceneID.GameScene)
            && boardManager != null
            && boardManager.IsInitialized
            && gameServiceManager != null
            && gameServiceManager.IsInitialized
            && AppState._gameSimHandler != null;
    }

    public static async Task<bool> EnsureBootstrapReadyAsync(string battleId)
    {
        if (IsBootstrapReady())
            return false;

        Stage(battleId, "lobby_bootstrap");
        Data.ResetRunData();
        if (!SceneLoader.IsSceneLoaded(SceneID.GameScene))
        {
            Stage(battleId, "load_game_scene");
            await Il2CppTasks.AwaitAsync(
                SceneLoader.LoadScene(
                    SceneID.GameScene,
                    true,
                    false,
                    null,
                    null,
                    Il2CppSystem.Threading.CancellationToken.None
                ),
                "SceneLoader.LoadScene(GameScene)",
                SceneTimeout
            );
        }
        if (!SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
        {
            Stage(battleId, "load_gameplay_loading");
            await Il2CppTasks.AwaitAsync(
                SceneLoader.LoadSceneAdditive(
                    SceneID.GameplayLoading,
                    Il2CppSystem.Threading.CancellationToken.None
                ),
                "SceneLoader.LoadSceneAdditive(GameplayLoading)",
                SceneTimeout
            );
        }
        Stage(battleId, "wait_game_service_manager");
        await AndroidReplayGameServices.WaitUntilAsync(
            () => GameServiceManager.Instance != null,
            BootstrapTimeout
        );
        Stage(battleId, "bootstrap_managers");
        await AndroidReplayGameServices.BootstrapManagersAsync();
        AndroidReplayGameServices.EnsureAppStateHandlersInitialized();
        Stage(battleId, "wait_bootstrap_ready");
        await AndroidReplayGameServices.WaitUntilAsync(IsBootstrapReady, BootstrapTimeout);
        await Il2CppTasks.AwaitAsync(
            SceneLoader.SetActiveScene(
                SceneID.GameScene,
                Il2CppSystem.Threading.CancellationToken.None
            ),
            "SceneLoader.SetActiveScene(GameScene)",
            SceneTimeout
        );
        SceneLoader.LoadingComplete();
        if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await Il2CppTasks.AwaitAsync(
                SceneLoader.UnloadScene(
                    SceneID.GameplayLoading,
                    Il2CppSystem.Threading.CancellationToken.None
                ),
                "SceneLoader.UnloadScene(GameplayLoading)",
                SceneTimeout
            );
        // Entering GameScene mutes sound effects until the loading-screen fade-out. Bootstrap skips
        // the screen, so signal the fade-out directly.
        Events.LoadingScreenFadeOut.Trigger();
        Stage(battleId, "lobby_bootstrap_ready");
        return true;
    }

    public static AndroidReplayBootstrapContext ResolveDependencies()
    {
        var network = AndroidReplayGameServices.RequireNetworkManager();
        var processor = AndroidReplayGameServices.RequireProcessor(network);
        AndroidReplayGameServices.EnsureAppStateHandlersInitialized(processor);
        return new AndroidReplayBootstrapContext(
            processor,
            AndroidReplayGameServices.RequireGameSimHandler()
        );
    }

    public static async Task InjectSavedReplayAsync(
        AndroidReplayBootstrapContext context,
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome,
        Func<ReplayPlaybackPublishOutcome> publishStarting
    )
    {
        var battleId = outcome.BattleId;
        using var tracking = AndroidReplayPresentation.Tasks.BeginTracking();
        Stage(battleId, "inject");
        AndroidReplaySavedState.Normalize(manifest, sequence);
        Quality(
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
            () => AndroidReplayPlaybackUi.EnsureSequencePlayerAttributes(sequence, outcome)
        );
        context.Processor.LastCombatSequence = sequence;
        AndroidReplayGameServices.HandleSpawnMessage(
            context.Processor,
            context.GameSimHandler,
            sequence.SpawnMessage
        );
        Quality(
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
            () => AndroidReplayPlaybackUi.EnsureRunPlayerAttributes(outcome)
        );
        Quality(
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
            () => AndroidReplayPlaybackUi.RestoreRecordedPlayerAttributes(manifest, outcome)
        );
        Quality(
            outcome,
            ReplayPlaybackReasonCode.PlayerSnapshotUnavailable,
            () =>
                AndroidReplaySavedState.RehydratePlayerCards(
                    manifest,
                    sequence.SpawnMessage,
                    outcome
                )
        );
        Quality(
            outcome,
            ReplayPlaybackReasonCode.OpponentSnapshotUnavailable,
            () =>
                AndroidReplaySavedState.RehydrateOpponentCards(
                    manifest,
                    sequence.SpawnMessage,
                    outcome
                )
        );
        Quality(
            outcome,
            ReplayPlaybackReasonCode.PlayerSkillsUnavailable,
            () =>
                AndroidReplaySavedState.RehydratePlayerSkills(
                    manifest,
                    sequence.SpawnMessage,
                    outcome
                )
        );
        Quality(
            outcome,
            ReplayPlaybackReasonCode.OpponentSkillsUnavailable,
            () =>
                AndroidReplaySavedState.RehydrateOpponentSkills(
                    manifest,
                    sequence.SpawnMessage,
                    outcome
                )
        );
        await QualityAsync(
            outcome,
            ReplayPlaybackReasonCode.PresentationWarmupFailed,
            () =>
                AndroidReplaySavedState.RestoreOpeningEventsAsync(
                    context.GameSimHandler,
                    sequence.SpawnMessage,
                    outcome
                )
        );
        AndroidReplaySavedState.SanitizeSpawnEvents(sequence, battleId);
        Stage(battleId, "rebuild_skills");
        await AndroidReplayGameServices.RebuildSkillPresentationAsync();
        AndroidReplayGameServices.TriggerCombatSequenceCreated(context.Processor);
        await AndroidMainThread.Host.DelayAsync(TimeSpan.FromMilliseconds(50));
        Stage(battleId, "push_replay_state");
        await Il2CppTasks.AwaitAsync(
            AppState.TryPushState<ReplayState>(),
            "AppState.TryPushState<ReplayState>"
        );
        var replayState =
            AppState.CurrentState?.TryCast<ReplayState>()
            ?? throw new InvalidOperationException("ReplayState did not become active.");
        RequireBoardManager().ShowReplayAndRecapButtons(false, true);
        AndroidReplayPlaybackUi.HideEncounterPickerOverlays();
        AndroidReplayPlaybackUi.EnsureOpponentPortraitVisible();
        await QualityAsync(
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
            () => AndroidReplayPlaybackUi.PrepareHealthBarsAsync(outcome)
        );
        RequireBoardManager().ToggleOpponentPortrait(true);
        Stage(battleId, "wait_presentation");
        await AndroidReplayGameServices.WaitForPresentationReadyAsync();
        tracking.Dispose();
        Quality(
            outcome,
            ReplayPlaybackReasonCode.PresentationWarmupFailed,
            () => AndroidReplayPlaybackUi.RefreshPresentation(manifest, sequence, outcome)
        );
        AndroidReplayPlaybackUi.HideEncounterPickerOverlays();
        AndroidReplayPlaybackUi.EnsureOpponentPortraitVisible();
        AndroidReplayPlaybackUi.RefillOpponentHealthBar();
        var published = publishStarting();
        if (!published.Succeeded)
            throw new ReplayPlaybackStartException(
                ReplayPlaybackReasonCode.StartingPublishFailed,
                published.Exception
            );
        Stage(battleId, "replay");
        replayState.Replay();
        AndroidReplayPlaybackUi.EnsureOpponentPortraitVisible();
        RequireBoardManager().ShowReplayAndRecapButtons(false, true);
    }

    public static async Task<ReplayBootstrapRollback> RollbackBootstrapAsync(
        IReplayPlaybackOutcomeSink outcome
    )
    {
        Exception? cleanupFailure = null;
        try
        {
            // Native listeners must release run audio and effects before scene destruction
            // unregisters them.
            Events.RunInterrupted.Trigger();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
            outcome.ReportDegradation(ReplayPlaybackReasonCode.BootstrapRollbackFailed, exception);
        }

        try
        {
            AppState.Reset();
            Data.ResetRunData();
            var network = AndroidReplayGameServices.DisposeNetwork();
            if (!network.Succeeded)
            {
                cleanupFailure ??= network.Exception;
                outcome.ReportDegradation(
                    ReplayPlaybackReasonCode.SocketCleanupFailed,
                    network.Exception
                );
            }
            GameServiceManager.Instance?.PauseOrUnpauseGame(false);
            if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
                await Il2CppTasks.AwaitAsync(
                    SceneLoader.UnloadScene(
                        SceneID.GameplayLoading,
                        Il2CppSystem.Threading.CancellationToken.None
                    ),
                    "SceneLoader.UnloadScene(GameplayLoading)",
                    SceneTimeout
                );
            await Il2CppTasks.AwaitAsync(
                SceneLoader.LoadScene(
                    AndroidLobbyScene.Lobby,
                    true,
                    false,
                    null,
                    null,
                    Il2CppSystem.Threading.CancellationToken.None
                ),
                "SceneLoader.LoadScene(HeroSelectScene)",
                SceneTimeout
            );
            return cleanupFailure == null
                ? ReplayBootstrapRollback.Success
                : ReplayBootstrapRollback.Failure(cleanupFailure);
        }
        catch (Exception exception)
        {
            return ReplayBootstrapRollback.Failure(cleanupFailure ?? exception);
        }
    }

    private static BoardManager RequireBoardManager() =>
        BoardManager.Instance
        ?? throw new InvalidOperationException("BoardManager is unavailable.");

    private static void Stage(string battleId, string stage) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.replay.bootstrap_stage"),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short),
            ("stage", stage),
            ("active_scene", AndroidLobbyScene.Describe())
        );

    private static void Quality(
        IReplayPlaybackOutcomeSink outcome,
        ReplayPlaybackReasonCode reasonCode,
        Action action
    )
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(reasonCode, exception);
        }
    }

    private static async Task QualityAsync(
        IReplayPlaybackOutcomeSink outcome,
        ReplayPlaybackReasonCode reasonCode,
        Func<Task> action
    )
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(reasonCode, exception);
        }
    }
}

internal sealed record AndroidReplayBootstrapContext(
    NetMessageProcessor Processor,
    GameSimHandler GameSimHandler
);
