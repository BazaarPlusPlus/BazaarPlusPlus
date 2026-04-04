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
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI.Components;
using TheBazaar.UI.EncounterPicker;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
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
    private BattleUploadSqliteStore? _uploadStore;
    private CombatReplayPersistenceQueue? _persistenceQueue;
    private CombatReplayCaptureService? _captureService;
    private CombatReplayLoader? _loader;
    private CombatReplayController? _controller;
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

    public static void HideEncounterPickerOverlays()
    {
        HideObjectsOfType<EncounterPickerMapController>();
        HideObjectsOfType<InjectedEncounterPickerMapController>();

        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (
                transform?.gameObject != null
                && string.Equals(
                    transform.gameObject.name,
                    "EncounterPicker_Map(Clone)",
                    StringComparison.Ordinal
                )
            )
            {
                transform.gameObject.SetActive(false);
            }
        }
    }

    public static void EnsureOpponentPortraitVisible()
    {
        var replayPortrait = Instance?._replayTemporaryOpponentPortrait;
        if (replayPortrait != null)
        {
            if (Data.CurrentEncounterController != null)
                Data.CurrentEncounterController.ShowCard(show: false);

            replayPortrait.gameObject.SetActive(true);
            replayPortrait.ShowCard(show: true);
            return;
        }

        var encounterController = Data.CurrentEncounterController;
        if (encounterController?.gameObject == null)
            return;

        encounterController.gameObject.SetActive(true);
        encounterController.ShowCard(show: true);
    }

    public static async Task PrepareReplayHealthBarsAsync()
    {
        var bindings = await RefreshReplayHealthBarBindingsAsync();
        ShowReplayPlayerHealthBar(bindings.PlayerController);
        Data.PlayerExperienceBar?.ToggleExperienceBarAndText(isVisible: false);
        Events.TryShowEmptyOpponentHealthBar.Trigger();
    }

    public static void RefillReplayOpponentHealthBar()
    {
        Events.TryRefillOpponentHealthBar.Trigger();
    }

    private static async Task<ReplayBoardUiBindings> RefreshReplayHealthBarBindingsAsync()
    {
        var bindings = ResolveReplayBoardUiControllers();

        if (bindings.PlayerController != null)
            BindReplayBoardUiController(bindings.PlayerController, registerPlayerHealthBar: true);

        if (bindings.OpponentController != null)
            BindReplayBoardUiController(
                bindings.OpponentController,
                registerPlayerHealthBar: false
            );

        await Task.Delay(150);
        return bindings;
    }

    private static IEnumerable<BoardUIController> GetSceneBoardUiControllers()
    {
        return UnityEngine
            .Object.FindObjectsOfType<BoardUIController>(true)
            .Where(controller => controller != null && controller.gameObject.scene.rootCount > 0);
    }

    private static ReplayBoardUiBindings ResolveReplayBoardUiControllers()
    {
        var controllers = GetSceneBoardUiControllers().ToList();
        return new ReplayBoardUiBindings(
            SelectReplayBoardUiController(controllers, ECombatantId.Player, AnchorSide.Player),
            SelectReplayBoardUiController(controllers, ECombatantId.Opponent, AnchorSide.Opponent)
        );
    }

    private static BoardUIController? SelectReplayBoardUiController(
        IEnumerable<BoardUIController> controllers,
        ECombatantId combatantId,
        AnchorSide anchorSide
    )
    {
        var anchor = Singleton<BoardManager>.Instance?.GetAnchor(anchorSide, AnchorType.Portrait);
        return controllers
            .Where(controller => controller.combatantId == combatantId)
            .OrderByDescending(controller => controller.gameObject.activeInHierarchy)
            .ThenByDescending(controller => controller.isActiveAndEnabled)
            .ThenByDescending(HasActiveHealthBar)
            .ThenBy(controller => GetControllerAnchorDistance(controller, anchor))
            .FirstOrDefault();
    }

    private static bool HasActiveHealthBar(BoardUIController controller)
    {
        var healthBar = GetBoardUiHealthBar(controller) as Component;
        return healthBar?.gameObject.activeInHierarchy == true;
    }

    private static float GetControllerAnchorDistance(
        BoardUIController controller,
        Transform? anchor
    )
    {
        if (anchor == null)
            return float.MaxValue;

        return Vector3.SqrMagnitude(controller.transform.position - anchor.position);
    }

    private static void BindReplayBoardUiController(
        BoardUIController controller,
        bool registerPlayerHealthBar
    )
    {
        var player =
            controller.combatantId == ECombatantId.Player ? Data.Run?.Player : Data.Run?.Opponent;
        if (player == null)
            return;

        EnsureReplayHealthAttributes(player, controller.combatantId);

        if (InitializedReplayBoardUiControllers.Add(controller.GetInstanceID()))
            InvokeBoardUiMethod(controller, "Init", player);

        if (controller.combatantId == ECombatantId.Player)
            UnregisterPlayerPortraitPlacedHandler(controller);

        InvokeBoardUiMethod(controller, "SetBattlePlayer", player);
        ApplyBoardUiDividerConfig(controller);
        InitializeBoardUiHealthBar(controller, player);

        if (registerPlayerHealthBar && controller.combatantId == ECombatantId.Player)
            Data.RegisterPlayerHealthBar(controller);
    }

    private static void ShowReplayPlayerHealthBar(BoardUIController? playerController)
    {
        if (playerController != null)
        {
            if (Data.Run?.Player != null)
                EnsureReplayHealthAttributes(Data.Run.Player, ECombatantId.Player);

            InvokeBoardUiMethod(playerController, "SetBattlePlayer", Data.Run?.Player);
            InitializeBoardUiHealthBar(playerController, Data.Run?.Player);
            playerController.ShowEmptyPlayerHealthBar();
            RevealBoardUiHealthBar(playerController, showStatusNumbers: true);
            RecalculateHealthBarDividers(playerController, Data.Run?.Player);
            return;
        }

        Data.PlayerHealthBar?.ShowEmptyPlayerHealthBar();
    }

    private static void EnsureReplayHealthAttributes(object player, ECombatantId combatantId)
    {
        try
        {
            var attributesProperty = player
                .GetType()
                .GetProperty(
                    "Attributes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            if (
                attributesProperty?.GetValue(player)
                is not System.Collections.IDictionary attributes
            )
                return;

            if (attributes.Contains(EPlayerAttributeType.HealthMax))
                return;

            if (!attributes.Contains(EPlayerAttributeType.Health))
                return;

            var healthValue = Convert.ToInt32(attributes[EPlayerAttributeType.Health]);
            if (healthValue <= 0)
                return;

            attributes[EPlayerAttributeType.HealthMax] = healthValue;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to backfill replay HealthMax for {combatantId}: {ex.Message}"
            );
        }
    }

    private static void RecalculateHealthBarDividers(BoardUIController controller, object? player)
    {
        if (player == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        ApplyHealthBarMaxValue(healthBar, player);
    }

    private static void UnregisterPlayerPortraitPlacedHandler(BoardUIController controller)
    {
        try
        {
            var handlerMethod = controller
                .GetType()
                .GetMethod(
                    "HandleOnPlayerPortraitPlaced",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
            if (handlerMethod == null)
                return;

            var handler = Delegate.CreateDelegate(typeof(Action), controller, handlerMethod);
            var eventField = typeof(BoardManager).GetField(
                "_playerPortraitPlaced",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            if (eventField?.GetValue(null) is Action currentDelegate)
            {
                eventField.SetValue(null, (Action)Delegate.Remove(currentDelegate, handler));
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Failed to unregister PlayerPortraitPlaced handler: {ex.Message}"
            );
        }
    }

    private static void InitializeBoardUiHealthBar(BoardUIController controller, object? player)
    {
        if (player == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        var initMethod = healthBar
            .GetType()
            .GetMethod(
                "Init",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (initMethod == null)
            return;

        try
        {
            initMethod.Invoke(healthBar, new[] { player });
            ApplyHealthBarMaxValue(healthBar, player);
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Skipping health bar init for {controller.combatantId}: {ex.InnerException?.Message ?? ex.Message}"
            );
        }
    }

    private static void ApplyBoardUiDividerConfig(BoardUIController controller)
    {
        var healthBarDividerConfigField = controller
            .GetType()
            .GetField(
                "healthBarDividerConfigSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var dividerConfig =
            healthBarDividerConfigField?.GetValue(controller) as HealthBarDividerConfigSO;
        if (dividerConfig == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        InvokeOptionalMethod(healthBar, "SetDividerConfig", dividerConfig);
    }

    private static void ApplyHealthBarMaxValue(object healthBar, object player)
    {
        var healthMax = TryGetPlayerAttribute(player, EPlayerAttributeType.HealthMax);
        if (!healthMax.HasValue)
            return;

        var updateMaxHealth = healthBar
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "UpdateMaxHealth", StringComparison.Ordinal))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(uint)
                    && parameters[1].ParameterType == typeof(uint)
                    && parameters[2].ParameterType == typeof(bool);
            });
        if (updateMaxHealth == null)
            return;

        updateMaxHealth.Invoke(healthBar, new object[] { healthMax.Value, healthMax.Value, false });
    }

    private static uint? TryGetPlayerAttribute(object player, EPlayerAttributeType attributeType)
    {
        var attributesProperty = player
            .GetType()
            .GetProperty(
                "Attributes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (attributesProperty?.GetValue(player) is not System.Collections.IDictionary attributes)
            return null;
        if (!attributes.Contains(attributeType))
            return null;

        return Convert.ToUInt32(attributes[attributeType]);
    }

    private static void RevealBoardUiHealthBar(BoardUIController controller, bool showStatusNumbers)
    {
        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        InvokeOptionalMethod(healthBar, "ToggleBarParent", true);
        InvokeOptionalMethod(healthBar, "ToggleStatusNumbers", showStatusNumbers);
        InvokeOptionalMethod(healthBar, "RefillHealthBar", 1f);
    }

    private static object? GetBoardUiHealthBar(BoardUIController controller)
    {
        return controller
            .GetType()
            .GetField(
                "HealthBar",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(controller);
    }

    private static void InvokeOptionalMethod(object target, string methodName, object argument)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    return false;

                var parameters = candidate.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType.IsInstanceOfType(argument);
            });

        method?.Invoke(target, new[] { argument });
    }

    private static void InvokeBoardUiMethod(
        BoardUIController controller,
        string methodName,
        object? argument = null
    )
    {
        var methods = controller
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));

        MethodInfo? targetMethod = null;
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (argument == null)
            {
                if (parameters.Length == 0)
                {
                    targetMethod = method;
                    break;
                }

                continue;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument))
            {
                targetMethod = method;
                break;
            }
        }

        if (targetMethod == null)
            return;

        if (argument == null)
        {
            targetMethod.Invoke(controller, null);
            return;
        }

        targetMethod.Invoke(controller, new[] { argument });
    }

    private void Awake()
    {
        Instance = this;
        var runLogDatabasePath =
            BppRuntimeHost.Paths.RunLogDatabasePath
            ?? throw new InvalidOperationException("Run log database path is not initialized.");
        var combatReplayDirectoryPath =
            BppRuntimeHost.Paths.CombatReplayDirectoryPath
            ?? throw new InvalidOperationException(
                "Combat replay directory path is not initialized."
            );
        _battleCatalog = new PvpBattleCatalog(runLogDatabasePath);
        _payloadStore = new CombatReplayPayloadStore(combatReplayDirectoryPath);
        _uploadStore = new BattleUploadSqliteStore(runLogDatabasePath, combatReplayDirectoryPath);
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

        if (BppRuntimeHost.RunContext.IsInGameRun)
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
                BppRuntimeHost.RunContext.CurrentServerRunId
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

            BppRuntimeHost.EventBus.Publish(new PvpBattleRecorded { Manifest = result.Manifest });
            _uploadStore?.MarkReplayDirty(result.Manifest.BattleId);
            BppLog.Info(
                "CombatReplayRuntime",
                $"Saved combat replay {result.Manifest.BattleId} for run={result.Manifest.RunId ?? "unknown"}"
            );
        }

        if (processedAny && _persistenceQueue?.HasPendingPersistence == false)
        {
            BppRuntimeHost.EventBus.Publish(new CombatReplayPersistenceDrained());
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
            BppRuntimeHost.RunLifecycle.RefreshRunStateFromCurrentState();
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

    private static async Task<bool> EnsureReplayBootstrapReadyAsync()
    {
        if (IsReplayBootstrapReady())
            return false;

        BppLog.Info("CombatReplayRuntime", "Bootstrapping gameplay scene for lobby replay.");
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

        await WaitUntilAsync(
            () => Singleton<GameServiceManager>.Instance != null,
            timeout: TimeSpan.FromSeconds(20)
        );
        await BootstrapReplayManagersAsync();
        EnsureReplayAppStateHandlersInitialized();
        await WaitUntilAsync(IsReplayBootstrapReady, timeout: TimeSpan.FromSeconds(20));

        await SceneLoader.SetActiveScene(SceneID.GameScene);
        SceneLoader.LoadingComplete();
        if (SceneLoader.IsSceneLoaded(SceneID.GameplayLoading))
            await SceneLoader.UnloadScene(SceneID.GameplayLoading);

        BppLog.Info("CombatReplayRuntime", "Replay bootstrap scene environment is ready.");
        return true;
    }

    private static async Task BootstrapReplayManagersAsync()
    {
        var runManager = Services.Get<RunManager>();
        if (runManager == null)
            throw new InvalidOperationException("RunManager is unavailable.");

        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null)
            throw new InvalidOperationException("GameServiceManager is unavailable.");

        if (Singleton<BoardManager>.Instance != null && gameServiceManager.IsInitialized)
            return;

        var boardReferenceField = typeof(RunManager).GetField(
            "_baseBoardReference",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        var boardReference =
            boardReferenceField?.GetValue(runManager) as AssetReference
            ?? throw new MissingFieldException(typeof(RunManager).FullName, "_baseBoardReference");

        var boardBuilder = new BoardBuilder();
        runManager.BoardBuilder = boardBuilder;
        var boardManager = await boardBuilder.SetUpBoard(boardReference);
        await gameServiceManager.Init(boardManager);
    }

    private static ReplayBootstrapContext ResolveReplayDependencies()
    {
        var socketBehavior = EnsureSocketBehavior();
        var processor = GetProcessor(socketBehavior);
        EnsureReplayAppStateHandlersInitialized(processor);

        var gameSimHandler = GetGameSimHandler();
        var bootstrapContext = new ReplayBootstrapContext(
            socketBehavior,
            processor,
            gameSimHandler,
            CreateSetLastCombatSequence(processor),
            CreateHandleSpawnMessageAsync(processor, gameSimHandler),
            CreateTriggerCombatSequenceCreated(processor)
        );
        BppLog.Info("CombatReplayRuntime", "Replay bootstrap dependencies resolved.");
        return bootstrapContext;
    }

    private static async Task TryInjectSavedReplayAsync(
        ReplayBootstrapContext bootstrapContext,
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId
    )
    {
        bootstrapContext.SetLastCombatSequence(sequence);
        await bootstrapContext.HandleSpawnMessageAsync(sequence.SpawnMessage);
        RehydrateSavedReplayPlayerCards(manifest, sequence.SpawnMessage);
        RehydrateSavedReplayOpponentCards(manifest, sequence.SpawnMessage);
        RehydrateSavedReplayPlayerSkills(manifest, sequence.SpawnMessage);
        RehydrateSavedReplayOpponentSkills(manifest, sequence.SpawnMessage);
        await RebuildSavedReplaySkillPresentationAsync();
        bootstrapContext.TriggerCombatSequenceCreated();
        await Task.Delay(50);
        await AppState.TryPushState<ReplayState>();
        if (AppState.CurrentState is not ReplayState replayState)
            throw new InvalidOperationException("ReplayState did not become active.");
        HideEncounterPickerOverlays();
        EnsureOpponentPortraitVisible();
        await PrepareReplayHealthBarsAsync();
        Singleton<BoardManager>.Instance.ToggleOpponentPortrait(isVisible: true);
        await WaitForReplayPresentationReadyAsync();
        await WarmReplayPresentationAssetsAsync(manifest, sequence);
        WarmReplayAudioBanks();
        HideEncounterPickerOverlays();
        EnsureOpponentPortraitVisible();
        RefillReplayOpponentHealthBar();
        replayState.Replay();
        EnsureOpponentPortraitVisible();
        Singleton<BoardManager>.Instance.ShowReplayAndRecapButtons(show: false, deactivate: true);

        BppLog.Info("CombatReplayRuntime", $"Saved replay injection completed for {battleId}.");
    }

    private static void RehydrateSavedReplayPlayerCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain player-hand snapshots; player cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateSavedReplayCards(capture.Items, spawnMessage, Data.Run?.Player);
    }

    private static void RehydrateSavedReplayOpponentCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain opponent-hand snapshots; opponent cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateSavedReplayCards(capture.Items, spawnMessage, Data.Run?.Opponent);
    }

    private static void RehydrateSavedReplayPlayerSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain player-skill snapshots; player skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSavedReplaySkillCards(capture.Items, spawnMessage, Data.Run?.Player);
        ReplaceSkillCollection(Data.Run?.Player, skills);
    }

    private static void RehydrateSavedReplayOpponentSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay {manifest.BattleId} does not contain opponent-skill snapshots; opponent skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSavedReplaySkillCards(
            capture.Items,
            spawnMessage,
            Data.Run?.Opponent
        );
        ReplaceSkillCollection(Data.Run?.Opponent, skills);
    }

    private static void RehydrateSavedReplayCards(
        IEnumerable<CombatReplayCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;
        }
    }

    private static List<SkillCard> RehydrateSavedReplaySkillCards(
        IEnumerable<CombatReplayCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        var skills = new List<SkillCard>();
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;

            if (card is SkillCard skillCard)
                skills.Add(skillCard);
        }

        return skills;
    }

    private static void ApplySnapshotFallback(Card card, CombatReplayCardSnapshot snapshot)
    {
        if (snapshot.Attributes != null && snapshot.Attributes.Count > 0)
        {
            foreach (var entry in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        entry.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                    card.Attributes[attributeType] = entry.Value;
            }
        }

        if (snapshot.Tags != null && snapshot.Tags.Count > 0)
        {
            card.Tags = snapshot
                .Tags.Select(tag =>
                    Enum.TryParse<ECardTag>(tag, ignoreCase: false, out var parsedTag)
                        ? (ECardTag?)parsedTag
                        : null
                )
                .Where(tag => tag.HasValue)
                .Select(tag => tag!.Value)
                .ToHashSet();
        }

        if (
            !string.IsNullOrWhiteSpace(snapshot.Tier)
            && Enum.TryParse<ETier>(snapshot.Tier, ignoreCase: false, out var tier)
        )
            card.Tier = tier;

        if (
            card is ItemCard itemCard
            && !string.IsNullOrWhiteSpace(snapshot.Enchant)
            && Enum.TryParse<EEnchantmentType>(
                snapshot.Enchant,
                ignoreCase: false,
                out var enchantment
            )
        )
            itemCard.Enchantment = enchantment;
    }

    private static void ReplaceSkillCollection(object? combatant, IReadOnlyList<SkillCard> skills)
    {
        if (combatant == null)
            return;

        var skillsProperty = combatant
            .GetType()
            .GetProperty(
                "Skills",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (skillsProperty == null)
            return;

        if (skillsProperty.CanWrite)
        {
            skillsProperty.SetValue(combatant, skills.ToList());
            return;
        }

        if (skillsProperty.GetValue(combatant) is System.Collections.IList list)
        {
            list.Clear();
            foreach (var skill in skills)
                list.Add(skill);
        }
    }

    private static async Task RollbackReplayBootstrapAsync()
    {
        try
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Replay bootstrap failed. Resetting replay state and returning to lobby."
            );
            AppState.Reset();
            Data.ResetRunData();
            DisposeSocketBehavior();

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
            BppLog.Error("CombatReplayRuntime", $"Failed to roll back replay bootstrap: {ex}");
        }
    }

    private static bool IsReplayBootstrapReady()
    {
        return SceneLoader.IsSceneLoaded(SceneID.GameScene)
            && Singleton<BoardManager>.Instance != null
            && Singleton<BoardManager>.Instance.IsInitialized
            && Singleton<GameServiceManager>.Instance != null
            && Singleton<GameServiceManager>.Instance.IsInitialized
            && TryGetAppStateField<GameSimHandler>("_gameSimHandler") != null;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out while bootstrapping replay environment.");

            await Task.Delay(100);
        }
    }

    private static async Task WaitForReplayPresentationReadyAsync()
    {
        await WaitUntilAsync(
            () =>
            {
                var boardManager = Singleton<BoardManager>.Instance;
                if (boardManager == null || !boardManager.IsInitialized)
                    return false;

                var playerSkillPresentationReady =
                    Data.PlayerSkillPresentationManager == null
                    || !Data.PlayerSkillPresentationManager.IsUpdatingSkillBoard;
                var opponentSkillPresentationReady =
                    Data.OpponentSkillPresenationManager == null
                    || !Data.OpponentSkillPresenationManager.IsUpdatingSkillBoard;

                return !boardManager.StorageMoving
                    && !boardManager.IsUpdatingBoard
                    && !boardManager.IsUpdatingSkillBoard
                    && playerSkillPresentationReady
                    && opponentSkillPresentationReady
                    && !boardManager.isUpdatingPresentation
                    && !boardManager.IsCarpetUnrolling
                    && !boardManager.HasCardsToReveal();
            },
            timeout: TimeSpan.FromSeconds(5)
        );

        // Let one more frame pass so ReplayState.OnEnter fire-and-forget spawn work can settle
        // before combat sim playback starts.
        await Task.Delay(100);
    }

    private static async Task RebuildSavedReplaySkillPresentationAsync()
    {
        var playerSkills = Data.Run?.Player?.Skills?.Cast<Card>().ToList() ?? new List<Card>();
        var opponentSkills = Data.Run?.Opponent?.Skills?.Cast<Card>().ToList() ?? new List<Card>();

        if (Data.PlayerSkillPresentationManager != null)
            await Data.PlayerSkillPresentationManager.Initialize(playerSkills);

        if (Data.OpponentSkillPresenationManager != null)
            await Data.OpponentSkillPresenationManager.Initialize(opponentSkills);
    }

    private static readonly ECardSize[] ReplayWarmupCardSizes =
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

    private static async Task WarmReplayPresentationAssetsAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayWarmupStats();
        await WarmReplayAssetLoaderAsync(manifest, sequence, stats);
        await WarmReplayCombatVfxAsync(sequence, stats);
        stopwatch.Stop();
        BppLog.Info(
            "CombatReplayRuntime",
            $"Saved replay warmup finished in {stopwatch.ElapsedMilliseconds}ms "
                + $"sharedAssets(preloaded={stats.SharedAssetsPreloaded}, skipped={stats.SharedAssetsSkipped}) "
                + $"cards(preloaded={stats.CardsPreloaded}, skipped={stats.CardsSkipped}, failed={stats.CardsFailed}) "
                + $"overrideAssets(preloaded={stats.OverrideAssetsPreloaded}, skipped={stats.OverrideAssetsSkipped}, failed={stats.OverrideAssetsFailed}) "
                + $"combatVfx(prewarmed={stats.VfxPrewarmed}, skipped={stats.VfxSkipped}, failed={stats.VfxFailed})"
        );
    }

    private static async Task WarmReplayAssetLoaderAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        if (assetLoader == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Saved replay visual warmup skipped because AssetLoader is unavailable."
            );
            return;
        }

        if (TryReserveReplaySharedAssetsPreload())
        {
            try
            {
                await assetLoader.PreloadAssets();
                stats.SharedAssetsPreloaded++;
            }
            catch (Exception ex)
            {
                ReleaseReplaySharedAssetsPreload();
                BppLog.Warn(
                    "CombatReplayRuntime",
                    $"Saved replay asset preload failed: {ex.Message}"
                );
            }
        }
        else
        {
            stats.SharedAssetsSkipped++;
        }

        var preloadRequests = new Dictionary<string, (Guid TemplateId, ECardSize Size)>(
            StringComparer.Ordinal
        );

        foreach (var snapshot in EnumerateReplayItemSnapshots(manifest))
        {
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;

            var key = $"{templateId:N}:{snapshot.Size}";
            preloadRequests.TryAdd(key, (templateId, snapshot.Size));
        }

        var cardSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var cardWarmupTasks = preloadRequests.Select(request =>
            WarmReplayCardAsync(assetLoader, request.Key, request.Value, cardSemaphore, stats)
        );
        await Task.WhenAll(cardWarmupTasks);

        var overrideSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var overrideWarmupTasks = sequence
            .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(overrideKey =>
                WarmReplayOverrideAssetAsync(assetLoader, overrideKey, overrideSemaphore, stats)
            );
        await Task.WhenAll(overrideWarmupTasks);
    }

    private static async Task WarmReplayCardAsync(
        AssetLoader assetLoader,
        string cacheKey,
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveReplayCacheKey(ReplayPreloadedCardKeys, cacheKey))
        {
            stats.CardsSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            await assetLoader.PreloadCard(request.TemplateId, request.Size);
            stats.CardsPreloaded++;
        }
        catch (Exception ex)
        {
            ReleaseReplayCacheKey(ReplayPreloadedCardKeys, cacheKey);
            stats.CardsFailed++;
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay card preload failed for template={request.TemplateId} size={request.Size}: {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task WarmReplayOverrideAssetAsync(
        AssetLoader assetLoader,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveReplayCacheKey(ReplayPreloadedOverrideKeys, overrideKey))
        {
            stats.OverrideAssetsSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByAddress<GameObject>(overrideKey);
            stats.OverrideAssetsPreloaded++;
        }
        catch (Exception ex)
        {
            ReleaseReplayCacheKey(ReplayPreloadedOverrideKeys, overrideKey);
            stats.OverrideAssetsFailed++;
            BppLog.Debug(
                "CombatReplayRuntime",
                $"Saved replay override VFX preload skipped for '{overrideKey}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static IEnumerable<CombatReplayCardSnapshot> EnumerateReplayItemSnapshots(
        PvpBattleManifest manifest
    )
    {
        foreach (
            var capture in new[] { manifest.Snapshots.PlayerHand, manifest.Snapshots.OpponentHand }
        )
        {
            if (capture.Status == PvpBattleCaptureStatus.Missing || capture.Items == null)
                continue;

            foreach (var snapshot in capture.Items)
            {
                if (snapshot?.Type == ECardType.Item)
                    yield return snapshot;
            }
        }
    }

    private static async Task WarmReplayCombatVfxAsync(
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        Services.TryGet<VFXManager>(out var vfxManager);
        if (assetLoader == null || vfxManager == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Saved replay combat VFX warmup skipped because replay asset services are unavailable."
            );
            return;
        }

        var actionTypes = sequence
            .CombatMessage.Data.Frames.SelectMany(frame =>
                frame?.Events ?? Enumerable.Empty<ICombatSimEvent>()
            )
            .OfType<CombatSimEventEffectExecuted>()
            .Select(evt => DTOUtils.GetActionType(evt.ActionType))
            .Where(action => action != ActionType.Unknown)
            .Distinct()
            .ToList();
        var vfxSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var vfxTasks = new List<Task>();

        foreach (var action in actionTypes)
        {
            vfxTasks.Add(
                WarmReplayActionVfxAsync(assetLoader, vfxManager, action, vfxSemaphore, stats)
            );
        }

        foreach (
            var overrideKey in sequence
                .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
        )
        {
            vfxTasks.Add(
                WarmReplayOverrideVfxAsync(
                    assetLoader,
                    vfxManager,
                    actionTypes,
                    overrideKey,
                    vfxSemaphore,
                    stats
                )
            );
        }
        await Task.WhenAll(vfxTasks);
    }

    private static async Task WarmReplayActionVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        ActionType action,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetReplayVfxConfig(vfxManager);
        if (vfxConfig == null)
        {
            await WarmReplayVfxReferenceAsync(
                assetLoader,
                vfxManager.GetVFX(action),
                semaphore,
                stats
            );
            return;
        }

        if (TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in ReplayWarmupCardSizes)
            {
                await WarmReplayVfxReferenceAsync(
                    assetLoader,
                    TryGetMappedActionVfx(vfxConfig, size, action),
                    semaphore,
                    stats
                );
            }
        }

        await WarmReplayVfxReferenceAsync(assetLoader, vfxManager.GetVFX(action), semaphore, stats);
    }

    private static async Task WarmReplayOverrideVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetReplayVfxConfig(vfxManager);
        if (vfxConfig == null)
            return;

        foreach (var action in actionTypes)
        {
            foreach (var size in ReplayWarmupCardSizes)
            {
                await WarmReplayVfxReferenceAsync(
                    assetLoader,
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey),
                    semaphore,
                    stats
                );
            }
        }
    }

    private static object? GetReplayVfxConfig(VFXManager vfxManager)
    {
        return vfxManager
            .GetType()
            .GetField(
                "vfxManagerSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(vfxManager);
    }

    private static bool TryIsActionAttributeMapped(object vfxConfig, ActionType action)
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "IsActionAttributeMapped",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        return method?.Invoke(vfxConfig, new object[] { action }) as bool? == true;
    }

    private static AssetReference? TryGetMappedActionVfx(
        object vfxConfig,
        ECardSize size,
        ActionType action
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ECardSize), typeof(ActionType) },
                null
            );
        return method?.Invoke(vfxConfig, new object[] { size, action }) as AssetReference;
    }

    private static async Task<AssetReference?> TryGetOverrideActionVfxAsync(
        object vfxConfig,
        ActionType action,
        ECardSize size,
        string overrideKey
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetActionOverrideVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ActionType), typeof(ECardSize), typeof(string) },
                null
            );
        if (method == null)
            return null;

        var taskObject = method.Invoke(vfxConfig, new object[] { action, size, overrideKey });
        if (taskObject is Task<AssetReference> typedTask)
            return await typedTask;

        if (taskObject is not Task task)
            return taskObject as AssetReference;

        await task;
        return task.GetType()
                .GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(task) as AssetReference;
    }

    private static async Task WarmReplayVfxReferenceAsync(
        AssetLoader assetLoader,
        AssetReference? assetReference,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            return;

        var key = !string.IsNullOrWhiteSpace(assetReference.AssetGUID)
            ? assetReference.AssetGUID
            : assetReference.ToString();
        if (!TryReserveReplayCacheKey(ReplayPrewarmedVfxKeys, key))
        {
            stats.VfxSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByReference<GameObject>(assetReference);
            stats.VfxPrewarmed++;
        }
        catch (Exception ex)
        {
            ReleaseReplayCacheKey(ReplayPrewarmedVfxKeys, key);
            stats.VfxFailed++;
            BppLog.Debug(
                "CombatReplayRuntime",
                $"Saved replay VFX warmup skipped for '{key}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool TryReserveReplaySharedAssetsPreload()
    {
        lock (ReplayWarmupCacheLock)
        {
            if (ReplaySharedAssetsPreloaded)
                return false;

            ReplaySharedAssetsPreloaded = true;
            return true;
        }
    }

    private static void ReleaseReplaySharedAssetsPreload()
    {
        lock (ReplayWarmupCacheLock)
        {
            ReplaySharedAssetsPreloaded = false;
        }
    }

    private static bool TryReserveReplayCacheKey(HashSet<string> cache, string key)
    {
        lock (ReplayWarmupCacheLock)
        {
            return cache.Add(key);
        }
    }

    private static void ReleaseReplayCacheKey(HashSet<string> cache, string key)
    {
        lock (ReplayWarmupCacheLock)
        {
            cache.Remove(key);
        }
    }

    private sealed class ReplayWarmupStats
    {
        public int SharedAssetsPreloaded;
        public int SharedAssetsSkipped;
        public int CardsPreloaded;
        public int CardsSkipped;
        public int CardsFailed;
        public int OverrideAssetsPreloaded;
        public int OverrideAssetsSkipped;
        public int OverrideAssetsFailed;
        public int VfxPrewarmed;
        public int VfxSkipped;
        public int VfxFailed;
    }

    private static void WarmReplayAudioBanks()
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup skipped because SoundManager is unavailable."
                );
                return;
            }

            var boardAssets = UnityEngine
                .Object.FindObjectsOfType<HeroBoardController>(true)
                .Where(controller =>
                    controller != null && controller.gameObject.scene.rootCount > 0
                )
                .Select(controller => controller.AssociatedDataSO)
                .Where(asset => asset != null)
                .Distinct()
                .ToList();

            if (boardAssets.Count == 0)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup found no HeroBoardController instances in the scene."
                );
                return;
            }

            foreach (var boardAsset in boardAssets)
            {
                WarmReplayAudioBank(soundManager, boardAsset!);
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn("CombatReplayRuntime", $"Saved replay audio warmup failed: {ex.Message}");
        }
    }

    private static void WarmReplayAudioBank(SoundManager soundManager, BoardAssetDataSO boardAsset)
    {
        if (string.IsNullOrWhiteSpace(boardAsset.boardBank))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Board '{boardAsset.name}' has no boardBank; replay SFX may be incomplete."
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(boardAsset.boardAssetBank))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Board '{boardAsset.name}' has no boardAssetBank; replay SFX may be incomplete."
            );
            return;
        }

        BppLog.Info(
            "CombatReplayRuntime",
            $"Warm replay audio bank: board='{boardAsset.name}', metadata='{boardAsset.boardBank}', asset='{boardAsset.boardAssetBank}'"
        );
        soundManager.LoadBank(
            FModBank.EBankType.SFX,
            boardAsset.boardBank,
            boardAsset.boardAssetBank
        );
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

    private async Task EnsureReplayTemporaryOpponentPortraitAsync(PvpBattleManifest manifest)
    {
        if (_replayTemporaryOpponentPortrait != null)
        {
            _replayTemporaryOpponentPortrait.gameObject.SetActive(true);
            _replayTemporaryOpponentPortrait.ShowCard(show: true);
            return;
        }

        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay temp portrait: board manager unavailable for battle={manifest.BattleId}"
            );
            return;
        }

        var hero = Data.SimPvpOpponent?.Hero;
        if (!hero.HasValue || hero.Value == EHero.Common)
        {
            if (!TryParseHeroName(manifest.Participants.OpponentHero, out var parsedHero))
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    $"Replay temp portrait: opponent hero unavailable for battle={manifest.BattleId}"
                );
                return;
            }

            hero = parsedHero;
        }

        var collectionManager = Services.Get<CollectionManager>();
        if (collectionManager == null)
            throw new InvalidOperationException("CollectionManager is unavailable.");

        var loadout =
            Data.SimPvpOpponent?.PlayerLoadout
            ?? new BazaarCollectionLoadout
            {
                accountId = manifest.Participants.OpponentAccountId ?? string.Empty,
                heroSkinIds = Array.Empty<string>(),
                cardSkinIds = Array.Empty<string>(),
            };

        var skinData =
            await collectionManager.GetEquippedHeroSkin(hero.Value, loadout)
            ?? await collectionManager.GetEquippedHeroSkin(hero.Value);
        if (skinData == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay temp portrait: skin load returned null for hero={hero.Value} battle={manifest.BattleId}"
            );
            return;
        }

        var anchor = boardManager.GetAnchor(AnchorSide.Opponent, AnchorType.Portrait);
        var portraitController = await LoadReplayHeroPortraitAsync(
            skinData,
            ResolveReplayPortraitTier(manifest),
            anchor
        );
        if (portraitController == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay temp portrait: portrait load failed for hero={hero.Value} battle={manifest.BattleId}"
            );
            return;
        }

        if (Data.CurrentEncounterController != null)
            Data.CurrentEncounterController.ShowCard(show: false);

        portraitController.gameObject.name = "ReplayOpponentPortrait";
        portraitController.gameObject.SetActive(true);
        portraitController.ShowCard(show: true);
        _replayTemporaryOpponentPortrait = portraitController;
    }

    private void CleanupReplayOpponentPortrait()
    {
        if (_replayTemporaryOpponentPortrait != null)
        {
            try
            {
                Destroy(_replayTemporaryOpponentPortrait.gameObject);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    $"Replay temp portrait cleanup failed: {ex.Message}"
                );
            }

            _replayTemporaryOpponentPortrait = null;
        }

        if (Data.CurrentEncounterController != null)
        {
            Data.CurrentEncounterController.gameObject.SetActive(true);
            Data.CurrentEncounterController.ShowCard(show: true);
        }
    }

    private static async Task<EncounterController?> LoadReplayHeroPortraitAsync(
        SkinAssetDataSO skinData,
        ETier tier,
        Transform parent
    )
    {
        var boardBuilderType = typeof(BoardBuilder);
        var loadMethod = boardBuilderType.GetMethod(
            "LoadHeroPortraitAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        if (loadMethod == null)
            throw new MissingMethodException(boardBuilderType.FullName, "LoadHeroPortraitAsync");

        var taskObject = loadMethod.Invoke(null, new object?[] { skinData, tier, parent, false });
        if (taskObject is Task<EncounterController> typedTask)
            return await typedTask;

        if (taskObject is not Task task)
            return taskObject as EncounterController;

        await task;
        return task.GetType()
                .GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(task) as EncounterController;
    }

    private static ETier ResolveReplayPortraitTier(PvpBattleManifest manifest)
    {
        if (
            !string.IsNullOrWhiteSpace(manifest?.Participants?.OpponentRank)
            && Enum.TryParse(manifest.Participants.OpponentRank.Trim(), true, out ETier tier)
        )
            return tier;

        return ETier.Bronze;
    }

    private void ApplyReplaySelectedHeroOverride(PvpBattleManifest manifest)
    {
        if (manifest?.Participants == null)
            return;

        if (!TryParseHeroName(manifest.Participants.PlayerHero, out var replayHero))
            return;

        _replayOriginalSelectedHero = Data.SelectedHero;
        if (_replayOriginalSelectedHero.Value == replayHero)
        {
            _replaySelectedHeroOverridden = false;
            _replayOriginalSelectedHero = null;
            return;
        }

        SetReplaySelectedHero(replayHero);
        _replaySelectedHeroOverridden = true;
    }

    private void RestoreReplaySelectedHeroOverride()
    {
        if (!_replaySelectedHeroOverridden || !_replayOriginalSelectedHero.HasValue)
            return;

        SetReplaySelectedHero(_replayOriginalSelectedHero.Value);
        _replaySelectedHeroOverridden = false;
        _replayOriginalSelectedHero = null;
    }

    private static void SetReplaySelectedHero(EHero hero)
    {
        var clientCacheType = typeof(Data).Assembly.GetType("TheBazaar.ClientCache", false);
        var runConfigField = clientCacheType?.GetField(
            "RunConfig",
            BindingFlags.Static | BindingFlags.Public
        );
        var runConfig = runConfigField?.GetValue(null);
        var setSelectedHeroMethod = runConfig
            ?.GetType()
            .GetMethod("SetSelectedHero", BindingFlags.Instance | BindingFlags.Public);
        if (setSelectedHeroMethod == null)
            throw new MissingMethodException("TheBazaar.RunConfigurationCache", "SetSelectedHero");

        setSelectedHeroMethod.Invoke(runConfig, new object[] { hero });
    }

    private static void EnsureReplayOpponentIdentity(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        if (manifest?.Participants == null)
            return;

        if (spawnMessage?.Data?.CurrentState?.PvpOpponent != null)
            return;

        if (!TryParseHeroName(manifest.Participants.OpponentHero, out var opponentHero))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay opponent hero was unavailable for battle {manifest.BattleId}."
            );
            return;
        }

        var opponentLoadout = new BazaarCollectionLoadout
        {
            accountId = manifest.Participants.OpponentAccountId ?? string.Empty,
            heroSkinIds = Array.Empty<string>(),
            cardSkinIds = Array.Empty<string>(),
        };

        Data.SimPvpOpponent = new SimPvpOpponent(
            manifest.Participants.OpponentName,
            null,
            null,
            TryParseRank(manifest.Participants.OpponentRank),
            manifest.Participants.OpponentRating ?? 0,
            null,
            null,
            null,
            manifest.Participants.OpponentLevel,
            opponentHero,
            opponentLoadout,
            null
        );
    }

    private static bool TryParseHeroName(string? heroName, out EHero hero)
    {
        if (!string.IsNullOrWhiteSpace(heroName))
        {
            var trimmed = heroName.Trim();
            if (Enum.TryParse(trimmed, ignoreCase: true, out hero))
                return true;
        }

        hero = default;
        return false;
    }

    private static ERank? TryParseRank(string? rank)
    {
        if (!string.IsNullOrWhiteSpace(rank) && Enum.TryParse(rank.Trim(), true, out ERank parsed))
            return parsed;

        return null;
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
