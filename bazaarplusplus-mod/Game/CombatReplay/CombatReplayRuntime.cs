#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Audio;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Upload;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI.Components;
using TheBazaar.UI.EncounterPicker;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed partial class CombatReplayRuntime : MonoBehaviour
{
    private static readonly HashSet<int> InitializedReplayBoardUiControllers = new();
    private static readonly object ReplayWarmupCacheLock = new();
    private static readonly HashSet<string> ReplayPreloadedCardKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReplayPreloadedOverrideKeys = new(
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> ReplayPrewarmedVfxKeys = new(StringComparer.Ordinal);
    private const int ReplayWarmupConcurrency = 4;
    private static bool ReplaySharedAssetsPreloaded;

    private PvpBattleCatalog? _battleCatalog;
    private CombatReplayPayloadStore? _payloadStore;
    private BattleReplaySyncStateStore? _replaySyncStateStore;
    private CombatReplayPersistenceQueue? _persistenceQueue;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
    private IBppServices? _services;
    private RunLifecycleModule? _runLifecycle;
    private bool _returnToMenuAfterReplay;
    private bool _bootstrappedReplayActive;
    private bool _isReplayStartInProgress;
    private bool _savedReplayPlaybackActive;
    private EncounterController? _replayTemporaryOpponentPortrait;
    private EHero? _replayOriginalSelectedHero;
    private bool _replaySelectedHeroOverridden;

    public static CombatReplayRuntime? Instance { get; private set; }

    public string? ActiveBattleId => _controller?.ActiveBattleId;

    public bool IsReplayPlaybackActive =>
        _savedReplayPlaybackActive || AppState.CurrentState is ReplayState;

    public bool IsSavedReplayPlaybackActive => _savedReplayPlaybackActive;

    public bool HasPendingPersistence => _persistenceQueue?.HasPendingPersistence == true;

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize(IBppServices services, RunLifecycleModule runLifecycle)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));
        InitializeCore();
    }

    private void InitializeCore()
    {
        var services = _services!;
        var runLogDatabasePath =
            services.Paths.RunLogDatabasePath
            ?? throw new InvalidOperationException("Run log database path is not initialized.");
        var combatReplayDirectoryPath =
            services.Paths.CombatReplayDirectoryPath
            ?? throw new InvalidOperationException(
                "Combat replay directory path is not initialized."
            );
        _battleCatalog = new PvpBattleCatalog(runLogDatabasePath);
        _payloadStore = new CombatReplayPayloadStore(combatReplayDirectoryPath);
        _replaySyncStateStore = new BattleReplaySyncStateStore(
            runLogDatabasePath,
            combatReplayDirectoryPath
        );
        _persistenceQueue = new CombatReplayPersistenceQueue(
            _payloadStore.Save,
            _battleCatalog.Save,
            _payloadStore.Delete
        );
        _captureService = new CombatReplayCaptureService();
        _loader = new CombatReplayLoader();
        _controller = new CombatReplayController(_battleCatalog, _payloadStore, _loader);
        CleanupOrphanedPayloads();
        Events.StateChanged.AddListener(OnStateChanged, this);
    }

    private void Update()
    {
        DrainPersistenceResults();
    }

    private void OnDestroy()
    {
        _persistenceQueue?.Dispose();
        DrainPersistenceResults();

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
        if (_isReplayStartInProgress)
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

    public void ObserveMessage(INetMessage message)
    {
        if (_captureService == null || _persistenceQueue == null)
            return;

        try
        {
            var artifact = _captureService.Accept(
                message,
                _services!.RunContext.CurrentServerRunId
            );
            if (artifact == null)
                return;

            var payload = artifact.Payload;
            var manifest = artifact.Manifest;
            _persistenceQueue.Enqueue(payload, manifest);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatReplayRuntime", $"Failed to capture combat replay: {ex}");
        }
    }

    private void DrainPersistenceResults()
    {
        var processedAny = false;
        while (_persistenceQueue?.TryDequeueResult(out var result) == true)
        {
            processedAny = true;
            if (!result.Succeeded)
            {
                BppLog.Error(
                    "CombatReplayRuntime",
                    $"Failed to persist combat replay {result.Manifest.BattleId}: {result.Error}"
                );
                continue;
            }

            _services!.EventBus.Publish(new PvpBattleRecorded { Manifest = result.Manifest });
            _replaySyncStateStore?.MarkReplayDirty(result.Manifest.BattleId);
            BppLog.Info(
                "CombatReplayRuntime",
                $"Saved combat replay {result.Manifest.BattleId} for run={result.Manifest.RunId ?? "unknown"}"
            );
        }

        if (processedAny && _persistenceQueue?.HasPendingPersistence == false)
        {
            _services!.EventBus.Publish(new CombatReplayPersistenceDrained());
        }
    }

    private void CleanupOrphanedPayloads()
    {
        if (_payloadStore == null || _battleCatalog == null)
            return;

        try
        {
            foreach (var battleId in _payloadStore.ListBattleIds())
            {
                if (_battleCatalog.TryLoad(battleId) != null)
                    continue;

                try
                {
                    _payloadStore.Delete(battleId);
                }
                catch (Exception ex)
                {
                    BppLog.Warn(
                        "CombatReplayRuntime",
                        $"Failed to delete orphaned combat replay payload {battleId}: {ex.Message}"
                    );
                }
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to scan combat replay payloads for orphan cleanup: {ex.Message}"
            );
        }
    }

    public bool ReplayLatest()
    {
        var latest = _controller?.GetLatestBattle();
        if (latest == null)
            return false;

        return ReplaySaved(latest.BattleId);
    }

    public bool ReplaySaved(string battleId)
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
        InitializedReplayBoardUiControllers.Clear();
        _savedReplayPlaybackActive = true;
        _ = StartReplayAsync(manifest, sequence, battleId);
        return true;
    }

    public bool ReplayImportedBattle(PvpBattleManifest manifest, PvpReplayPayload payload)
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
        InitializedReplayBoardUiControllers.Clear();
        _savedReplayPlaybackActive = true;
        _ = StartReplayAsync(manifest, sequence, manifest.BattleId);
        return true;
    }

    private async Task StartReplayAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId
    )
    {
        var attemptedBootstrapFromLobby = false;
        _isReplayStartInProgress = true;
        try
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            CleanupReplayOpponentPortrait();
            ApplyReplaySelectedHeroOverride(manifest);
            Data.ResetRunData();
            _runLifecycle!.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !IsReplayBootstrapReady();
            var bootstrappedFromLobby = await EnsureReplayBootstrapReadyAsync();
            _returnToMenuAfterReplay = bootstrappedFromLobby;
            var bootstrapContext = ResolveReplayDependencies();
            EnsureReplayOpponentIdentity(manifest, sequence.SpawnMessage);
            await EnsureReplayTemporaryOpponentPortraitAsync(manifest);
            await TryInjectSavedReplayAsync(bootstrapContext, manifest, sequence, battleId);
            _bootstrappedReplayActive = bootstrappedFromLobby;
            BppLog.Info("CombatReplayRuntime", $"Started replay for saved combat {battleId}");
        }
        catch (Exception ex)
        {
            _returnToMenuAfterReplay = false;
            _bootstrappedReplayActive = false;
            _savedReplayPlaybackActive = false;
            CleanupReplayOpponentPortrait();
            RestoreReplaySelectedHeroOverride();
            BppLog.Error("CombatReplayRuntime", $"Failed to start replay {battleId}: {ex}");
            if (attemptedBootstrapFromLobby)
                await RollbackReplayBootstrapAsync();
        }
        finally
        {
            _isReplayStartInProgress = false;
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (data == null)
            return;

        if (data.PreviousState is not ReplayState || data.CurrentState is ReplayState)
            return;

        RestoreReplaySelectedHeroOverride();
        _savedReplayPlaybackActive = false;
        CleanupReplayOpponentPortrait();
        InitializedReplayBoardUiControllers.Clear();

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

    private static object EnsureSocketBehavior()
    {
        var socketBehavior = TryGetSocketBehavior();
        if (socketBehavior != null)
            return socketBehavior;

        throw new InvalidOperationException("SocketBehavior is unavailable.");
    }

    private static object? TryGetSocketBehavior()
    {
        try
        {
            var replayHostType = ResolveReplayHostType();
            if (replayHostType == null)
                return null;

            var getInstanceMethod = replayHostType.GetMethod(
                "GetInstance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (getInstanceMethod == null)
                return null;

            return getInstanceMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to resolve SocketBehavior via runtime API: {ex.Message}"
            );
            return null;
        }
    }

    private static Type? ResolveReplayHostType()
    {
        return typeof(NetMessageProcessor).Assembly.GetType("Networking.NetworkManager", false)
            ?? typeof(NetMessageProcessor).Assembly.GetType("Networking.SocketBehavior", false)
            ?? FindType("Networking.NetworkManager")
            ?? FindType("Networking.SocketBehavior")
            ?? FindTypeByName("NetworkManager")
            ?? FindTypeByName("SocketBehavior");
    }

    private static void DisposeSocketBehavior()
    {
        try
        {
            var socketBehavior = TryGetSocketBehavior();
            if (socketBehavior == null)
                return;
            var disposeMethod = socketBehavior
                .GetType()
                .GetMethod(
                    "Dispose",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            disposeMethod?.Invoke(socketBehavior, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to dispose socket during replay rollback: {ex.Message}"
            );
        }
    }

    private static NetMessageProcessor GetProcessor(object? socketBehavior)
    {
        socketBehavior ??= EnsureSocketBehavior();

        var method = socketBehavior
            .GetType()
            .GetMethod(
                "GetProcessor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var processor = method?.Invoke(socketBehavior, null) as NetMessageProcessor;
        if (processor != null)
            return processor;

        throw new InvalidOperationException("SocketBehavior did not expose a NetMessageProcessor.");
    }

    private static void EnsureReplayAppStateHandlersInitialized(
        NetMessageProcessor? processor = null
    )
    {
        if (TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null)
            return;

        processor ??= TryGetAppStateField<NetMessageProcessor>("_messageProcessor");
        processor ??= GetProcessor(TryGetSocketBehavior());

        var sharedVariables = TryGetAppStateField<SharedVariablesSO>("_sharedVariablesSo");
        if (sharedVariables == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<SharedVariablesSO>())
            {
                sharedVariables = candidate;
                break;
            }
        }

        if (sharedVariables == null)
            throw new InvalidOperationException("SharedVariablesSO is unavailable.");

        AppState.Initialize(sharedVariables, processor);
    }

    private static GameSimHandler GetGameSimHandler()
    {
        return TryGetAppStateField<GameSimHandler>("_gameSimHandler")
            ?? throw new InvalidOperationException("GameSimHandler is unavailable.");
    }

    private static Action<CombatSequenceMessages> CreateSetLastCombatSequence(object processor)
    {
        var property = processor
            .GetType()
            .GetProperty(
                "LastCombatSequence",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (property == null)
            throw new MissingMemberException(processor.GetType().FullName, "LastCombatSequence");

        return sequence => property.SetValue(processor, sequence);
    }

    private static Action CreateTriggerCombatSequenceCreated(object processor)
    {
        return () =>
        {
            var field = processor
                .GetType()
                .GetField(
                    "CombatSequenceCreated",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            var action = field?.GetValue(processor) as Action;
            action?.Invoke();
        };
    }

    private static Func<NetMessageGameSim, Task> CreateHandleSpawnMessageAsync(
        NetMessageProcessor processor,
        GameSimHandler gameSimHandler
    )
    {
        return async spawnMessage =>
        {
            if (!processor.Handle(spawnMessage))
                throw new InvalidOperationException(
                    "NetMessageProcessor rejected the replay spawn message."
                );

            Data.UpdateFromGameSimAsync(spawnMessage);
            MarkGameSimMessageHandled(gameSimHandler, spawnMessage.MessageId);
        };
    }

    private static void MarkGameSimMessageHandled(GameSimHandler gameSimHandler, string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return;

        var handledMessagesField = gameSimHandler
            .GetType()
            .BaseType?.GetField("_handledMessages", BindingFlags.Instance | BindingFlags.NonPublic);
        if (handledMessagesField?.GetValue(gameSimHandler) is not List<string> handledMessages)
            throw new MissingFieldException(
                gameSimHandler.GetType().BaseType?.FullName,
                "_handledMessages"
            );

        if (!handledMessages.Contains(messageId))
            handledMessages.Add(messageId);
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(fullName, throwOnError: false);
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    private static Type? FindTypeByName(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, type => type != null)!;
            }
            catch
            {
                continue;
            }

            foreach (var candidate in types)
            {
                if (
                    candidate != null
                    && string.Equals(candidate.Name, typeName, StringComparison.Ordinal)
                )
                    return candidate;
            }
        }

        return null;
    }

    private static T? TryGetAppStateField<T>(string fieldName)
        where T : class
    {
        var field = typeof(AppState).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        return field?.GetValue(null) as T;
    }

    private sealed class ReplayBootstrapContext
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

    private sealed class ReplayBoardUiBindings
    {
        public ReplayBoardUiBindings(
            BoardUIController? playerController,
            BoardUIController? opponentController
        )
        {
            PlayerController = playerController;
            OpponentController = opponentController;
        }

        public BoardUIController? PlayerController { get; }

        public BoardUIController? OpponentController { get; }
    }

    private static void HideObjectsOfType<T>()
        where T : Component
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component?.gameObject != null)
                component.gameObject.SetActive(false);
        }
    }
}
