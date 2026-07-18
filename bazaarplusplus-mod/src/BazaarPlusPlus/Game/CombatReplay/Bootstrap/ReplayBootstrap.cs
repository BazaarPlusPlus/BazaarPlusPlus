#nullable enable

using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Warmup;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplayBootstrap
{
    internal static async Task<bool> EnsureBootstrapReadyAsync()
    {
        if (IsBootstrapReady())
            return false;

        Data.ResetRunData();
        if (!SceneLoader.IsSceneLoaded(SceneID.GameScene))
        {
            await SceneLoader.LoadScene(
                SceneID.GameScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
        }

        if (!SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.LoadSceneAdditive(SceneID.GameplayLoading);

        await BootstrapManagerInitializer.WaitUntilAsync(
            () => Singleton<GameServiceManager>.Instance != null,
            timeout: TimeSpan.FromSeconds(20)
        );
        await BootstrapManagerInitializer.BootstrapManagersAsync();
        AppStateHandlerInstaller.EnsureAppStateHandlersInitialized();
        await BootstrapManagerInitializer.WaitUntilAsync(
            IsBootstrapReady,
            timeout: TimeSpan.FromSeconds(20)
        );

        await SceneLoader.SetActiveScene(SceneID.GameScene);
        SceneLoader.LoadingComplete();
        if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.UnloadScene(SceneID.GameplayLoading);

        return true;
    }

    internal static ReplayBootstrapContext ResolveDependencies(IReplayPlaybackOutcomeSink outcome)
    {
        var socketBehavior = SocketBehaviorBridge.EnsureSocketBehavior(outcome);
        var processor = SocketBehaviorBridge.GetProcessor(socketBehavior);
        AppStateHandlerInstaller.EnsureAppStateHandlersInitialized(processor);

        var gameSimHandler = AppStateHandlerInstaller.GetGameSimHandler();
        var bootstrapContext = new ReplayBootstrapContext(
            socketBehavior,
            processor,
            gameSimHandler,
            SocketBehaviorBridge.CreateSetLastCombatSequence(processor),
            SocketBehaviorBridge.CreateHandleSpawnMessageAsync(processor, gameSimHandler),
            SocketBehaviorBridge.CreateTriggerCombatSequenceCreated(processor)
        );
        return bootstrapContext;
    }

    internal static async Task InjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome,
        Func<ReplayPlaybackPublishOutcome>? publishStarting = null
    )
    {
        ReplaySavedStateNormalizer.Normalize(manifest, sequence);
        ObserveQualityStep(
            () => PlayerAttributeRepairer.EnsureSequencePlayerAttributes(sequence, outcome),
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable
        );
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        ObserveQualityStep(
            () => PlayerAttributeRepairer.EnsureRunPlayerAttributes(outcome),
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable
        );
        ObserveQualityStep(
            () => PlayerAttributeRepairer.RestoreRecordedPlayerAttributes(manifest, outcome),
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable
        );
        ObserveQualityStep(
            () => SnapshotRehydrator.RehydratePlayerCards(manifest, sequence.SpawnMessage, outcome),
            outcome,
            ReplayPlaybackReasonCode.PlayerSnapshotUnavailable
        );
        ObserveQualityStep(
            () =>
                SnapshotRehydrator.RehydrateOpponentCards(manifest, sequence.SpawnMessage, outcome),
            outcome,
            ReplayPlaybackReasonCode.OpponentSnapshotUnavailable
        );
        ObserveQualityStep(
            () =>
                SnapshotRehydrator.RehydratePlayerSkills(manifest, sequence.SpawnMessage, outcome),
            outcome,
            ReplayPlaybackReasonCode.PlayerSkillsUnavailable
        );
        ObserveQualityStep(
            () =>
                SnapshotRehydrator.RehydrateOpponentSkills(
                    manifest,
                    sequence.SpawnMessage,
                    outcome
                ),
            outcome,
            ReplayPlaybackReasonCode.OpponentSkillsUnavailable
        );
        await ObserveQualityStepAsync(
            () =>
                ReplayOpeningStateRestorer.RestoreBeforeReplayAsync(
                    bootstrapContext.GameSimHandler,
                    sequence.SpawnMessage,
                    manifest,
                    outcome
                ),
            outcome,
            ReplayPlaybackReasonCode.PresentationWarmupFailed
        );
        SnapshotRehydrator.SanitizeSpawnEvents(sequence, outcome.BattleId);
        await AppStateHandlerInstaller.RebuildSkillPresentationAsync();
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState replayState)
            throw new InvalidOperationException("ReplayState did not become active.");
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);
        HealthBarBinder.HideEncounterPickerOverlays();
        HealthBarBinder.EnsureOpponentPortraitVisible();
        await ObserveQualityStepAsync(
            () => HealthBarBinder.PrepareHealthBarsAsync(outcome),
            outcome,
            ReplayPlaybackReasonCode.PlayerAttributesUnavailable
        );
        Singleton<BoardManager>.Instance.ToggleOpponentPortrait(isVisible: true);
        await AppStateHandlerInstaller.WaitForPresentationReadyAsync();
        await ObserveQualityStepAsync(
            () => PresentationWarmer.WarmPresentationAssetsAsync(manifest, sequence, outcome),
            outcome,
            ReplayPlaybackReasonCode.PresentationWarmupFailed
        );
        ObserveQualityStep(
            () => ReplayPresentationRestorer.Refresh(manifest, sequence, outcome),
            outcome,
            ReplayPlaybackReasonCode.PresentationWarmupFailed
        );
        ObserveQualityStep(
            () => ReplayOpeningStateRestorer.FinalizeAfterWarmup(outcome),
            outcome,
            ReplayPlaybackReasonCode.PresentationWarmupFailed
        );
        await ObserveQualityStepAsync(
            () => AudioBankWarmer.WarmAudioBanksAsync(outcome),
            outcome,
            ReplayPlaybackReasonCode.AudioWarmupFailed
        );
        HealthBarBinder.HideEncounterPickerOverlays();
        HealthBarBinder.EnsureOpponentPortraitVisible();
        HealthBarBinder.RefillOpponentHealthBar();
        if (publishStarting != null)
        {
            var publishOutcome = publishStarting();
            if (!publishOutcome.Succeeded)
                throw new ReplayPlaybackPublishException(
                    ReplayPlaybackReasonCode.StartingPublishFailed,
                    publishOutcome.Exception
                );
        }
        AudioBankWarmer.EnsureAudioReadyForPlayback(outcome);
        replayState.Replay();
        HealthBarBinder.EnsureOpponentPortraitVisible();
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);
    }

    private static void ObserveQualityStep(
        Action action,
        IReplayPlaybackOutcomeSink outcome,
        ReplayPlaybackReasonCode reasonCode
    )
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(reasonCode, ex);
        }
    }

    private static async Task ObserveQualityStepAsync(
        Func<Task> action,
        IReplayPlaybackOutcomeSink outcome,
        ReplayPlaybackReasonCode reasonCode
    )
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            outcome.ReportDegradation(reasonCode, ex);
        }
    }

    internal static async Task<ReplayBootstrapRollbackOutcome> RollbackBootstrapAsync(
        IReplayPlaybackOutcomeSink outcome
    )
    {
        try
        {
            Exception? cleanupFailure = null;
            ReplayOpeningStateRestorer.Cleanup();
            AppState.Reset();
            Data.ResetRunData();
            var socketCleanup = SocketBehaviorBridge.DisposeSocketBehavior(outcome);
            if (!socketCleanup.Succeeded)
            {
                cleanupFailure = socketCleanup.Exception;
                outcome.ReportDegradation(
                    ReplayPlaybackReasonCode.SocketCleanupFailed,
                    socketCleanup.Exception
                );
            }

            if (Singleton<GameServiceManager>.Instance != null)
                Singleton<GameServiceManager>.Instance.PauseOrUnpauseGame(toPauseOrUnpause: false);

            if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
                await SceneLoader.UnloadScene(SceneID.GameplayLoading);

            await SceneLoader.LoadScene(
                SceneID.HeroSelectScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
            return cleanupFailure == null
                ? ReplayBootstrapRollbackOutcome.Success()
                : ReplayBootstrapRollbackOutcome.Failure(cleanupFailure);
        }
        catch (Exception ex)
        {
            return ReplayBootstrapRollbackOutcome.Failure(ex);
        }
    }

    internal static bool IsBootstrapReady()
    {
        return SceneLoader.IsSceneLoaded(SceneID.GameScene)
            && Singleton<BoardManager>.Instance != null
            && Singleton<BoardManager>.Instance.IsInitialized
            && Singleton<GameServiceManager>.Instance != null
            && Singleton<GameServiceManager>.Instance.IsInitialized
            && TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null;
    }

    internal static T? TryGetAppStateField<T>(string fieldName)
        where T : class
    {
        var field = typeof(AppState).GetField(
            fieldName,
            System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
        );
        return field?.GetValue(null) as T;
    }
}

internal sealed class ReplayPlaybackPublishException : Exception
{
    internal ReplayPlaybackPublishException(
        ReplayPlaybackReasonCode reasonCode,
        Exception? innerException
    )
        : base("Replay lifecycle event publication failed.", innerException)
    {
        ReasonCode = reasonCode;
    }

    internal ReplayPlaybackReasonCode ReasonCode { get; }
}

internal readonly record struct ReplayBootstrapRollbackOutcome(bool Succeeded, Exception? Exception)
{
    internal static ReplayBootstrapRollbackOutcome Success() => new(true, null);

    internal static ReplayBootstrapRollbackOutcome Failure(Exception exception) =>
        new(false, exception ?? throw new ArgumentNullException(nameof(exception)));
}

internal sealed class ReplayBootstrapContext
{
    public ReplayBootstrapContext(
        object? socketBehavior,
        NetMessageProcessor processor,
        GameSimHandler gameSimHandler,
        Action<CombatSequenceMessages> setLastCombatSequence,
        Func<NetMessageGameSim, Task> handleSpawnMessageAsync,
        Action triggerCombatSequenceCreated
    )
    {
        SocketBehavior = socketBehavior;
        Processor = processor;
        GameSimHandler = gameSimHandler;
        SetLastCombatSequence = setLastCombatSequence;
        HandleSpawnMessageAsync = handleSpawnMessageAsync;
        TriggerCombatSequenceCreated = triggerCombatSequenceCreated;
    }

    public object? SocketBehavior { get; }

    public NetMessageProcessor Processor { get; }

    public GameSimHandler GameSimHandler { get; }

    public Action<CombatSequenceMessages> SetLastCombatSequence { get; }

    public Func<NetMessageGameSim, Task> HandleSpawnMessageAsync { get; }

    public Action TriggerCombatSequenceCreated { get; }
}
