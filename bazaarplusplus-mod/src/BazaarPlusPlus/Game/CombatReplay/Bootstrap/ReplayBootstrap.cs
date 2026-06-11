#nullable enable

using System;
using System.Threading.Tasks;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Warmup;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplayBootstrap
{
    internal static async Task<bool> EnsureBootstrapReadyAsync()
    {
        if (IsBootstrapReady())
            return false;

        BppLog.Info("ReplayBootstrap", "Bootstrapping gameplay scene for lobby replay.");
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

        BppLog.Info("ReplayBootstrap", "Replay bootstrap scene environment is ready.");
        return true;
    }

    internal static ReplayBootstrapContext ResolveDependencies()
    {
        var socketBehavior = SocketBehaviorBridge.EnsureSocketBehavior();
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
        BppLog.Info("ReplayBootstrap", "Replay bootstrap dependencies resolved.");
        return bootstrapContext;
    }

    internal static async Task InjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId,
        Action? onBeforeReplayPlayback = null
    )
    {
        PlayerAttributeRepairer.EnsureSequencePlayerAttributes(sequence);
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        PlayerAttributeRepairer.EnsureRunPlayerAttributes();
        SnapshotRehydrator.RehydratePlayerCards(manifest, sequence.SpawnMessage);
        SnapshotRehydrator.RehydrateOpponentCards(manifest, sequence.SpawnMessage);
        SnapshotRehydrator.RehydratePlayerSkills(manifest, sequence.SpawnMessage);
        SnapshotRehydrator.RehydrateOpponentSkills(manifest, sequence.SpawnMessage);
        SnapshotRehydrator.SanitizeSpawnEvents(sequence);
        await AppStateHandlerInstaller.RebuildSkillPresentationAsync();
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState replayState)
            throw new InvalidOperationException("ReplayState did not become active.");
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);
        HealthBarBinder.HideEncounterPickerOverlays();
        HealthBarBinder.EnsureOpponentPortraitVisible();
        await HealthBarBinder.PrepareHealthBarsAsync();
        Singleton<BoardManager>.Instance.ToggleOpponentPortrait(isVisible: true);
        await AppStateHandlerInstaller.WaitForPresentationReadyAsync();
        await PresentationWarmer.WarmPresentationAssetsAsync(manifest, sequence);
        await AudioBankWarmer.WarmAudioBanksAsync();
        HealthBarBinder.HideEncounterPickerOverlays();
        HealthBarBinder.EnsureOpponentPortraitVisible();
        HealthBarBinder.RefillOpponentHealthBar();
        onBeforeReplayPlayback?.Invoke();
        AudioBankWarmer.EnsureAudioReadyForPlayback();
        replayState.Replay();
        HealthBarBinder.EnsureOpponentPortraitVisible();
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);

        BppLog.Info("ReplayBootstrap", $"Saved replay injection completed for {battleId}.");
    }

    internal static async Task RollbackBootstrapAsync()
    {
        try
        {
            BppLog.Warn(
                "ReplayBootstrap",
                "Replay bootstrap failed. Resetting replay state and returning to lobby."
            );
            AppState.Reset();
            Data.ResetRunData();
            SocketBehaviorBridge.DisposeSocketBehavior();

            if (Singleton<GameServiceManager>.Instance != null)
                Singleton<GameServiceManager>.Instance.PauseOrUnpauseGame(toPauseOrUnpause: false);

            if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
                await SceneLoader.UnloadScene(SceneID.GameplayLoading);

            await SceneLoader.LoadScene(
                SceneID.HeroSelectScene,
                shouldUnloadCurrentScene: true,
                showLoadingScene: false
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("ReplayBootstrap", $"Failed to roll back replay bootstrap: {ex}");
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
