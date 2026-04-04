#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.RunLifecycle;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppRuntimeHost
{
    private static readonly BppRuntimeServices DetachedServices = new(
        new InMemoryBppEventBus(),
        new BppConfig(),
        new BppPathService(),
        new EmptyMonsterCatalog(),
        new RunContextStore(),
        new GameStateProbe()
    );
    private readonly ManualLogSource _logger;
    private readonly InMemoryBppEventBus _eventBus = new();
    private readonly BppConfig _config = new();
    private readonly BppPathService _paths = new();
    private readonly MonsterDatabase _monsterCatalog = new();
    private readonly RunContextStore _runContext = new();
    private readonly GameStateProbe _gameStateProbe = new();
    private readonly RunLifecycleModule _runLifecycle;
    private readonly CombatReplayModule _combatReplayModule;
    private readonly CombatStatusBarModule _combatStatusBarModule;
    private readonly BppFeatureRegistry _featureRegistry;

    public BppRuntimeHost(
        GameObject hostObject,
        ManualLogSource logger,
        ConfigFile configFile,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
    )
    {
        if (hostObject == null)
            throw new ArgumentNullException(nameof(hostObject));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));
        if (configFile == null)
            throw new ArgumentNullException(nameof(configFile));
        if (combatReplayRuntimeAccessor == null)
            throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));

        _logger = logger;
        _config.Initialize(configFile);
        _paths.Initialize();
        _monsterCatalog.Initialize();
        _runContext.Reset();
        Services = new BppRuntimeServices(
            _eventBus,
            _config,
            _paths,
            _monsterCatalog,
            _runContext,
            _gameStateProbe
        );
        _runLifecycle = new RunLifecycleModule(
            Services.EventBus,
            Services.GameStateProbe,
            Services.RunContext
        );
        _combatReplayModule = new CombatReplayModule(
            Services.EventBus,
            combatReplayRuntimeAccessor
        );
        _combatStatusBarModule = new CombatStatusBarModule(Services.EventBus, Services.RunContext);
        _featureRegistry = new BppFeatureRegistry();
        _featureRegistry.Register(_runLifecycle);
        _featureRegistry.Register(_combatReplayModule);
        _featureRegistry.Register(_combatStatusBarModule);
    }

    public static BppRuntimeHost? Current { get; private set; }

    public BppRuntimeServices Services { get; }

    public RunLifecycleModule LifecycleModule => _runLifecycle;

    public static IBppEventBus EventBus => Current?.Services.EventBus ?? DetachedServices.EventBus;

    public static ManualLogSource? Logger => Current?._logger;

    public static IBppConfig Config => Current?.Services.Config ?? DetachedServices.Config;

    public static IPathService Paths => Current?.Services.Paths ?? DetachedServices.Paths;

    public static IMonsterCatalog MonsterCatalog =>
        Current?.Services.MonsterCatalog ?? DetachedServices.MonsterCatalog;

    public static IRunContext RunContext =>
        Current?.Services.RunContext ?? DetachedServices.RunContext;

    public static IGameStateProbe GameStateProbe =>
        Current?.Services.GameStateProbe ?? DetachedServices.GameStateProbe;

    public static RunLifecycleModule RunLifecycle =>
        Current?._runLifecycle
        ?? throw new InvalidOperationException("Runtime host is not installed.");

    public void Install()
    {
        Current = this;
        BppLog.Info("RuntimeHost", "Installed runtime host");
    }

    public void Start()
    {
        _featureRegistry.Start();
        BppLog.Info("RuntimeHost", "Started runtime host");
    }

    public void Stop()
    {
        _featureRegistry.Stop();
        if (ReferenceEquals(Current, this))
            Current = null;
    }

    private sealed class EmptyMonsterCatalog : IMonsterCatalog
    {
        public bool TryGetByEncounterId(Guid encounterId, out MonsterInfo? monster)
        {
            monster = null;
            return false;
        }

        public bool TryGetByEncounterId(string encounterId, out MonsterInfo? monster)
        {
            monster = null;
            return false;
        }

        public bool TryGetByEncounterIdPrefix(string encounterIdPrefix, out MonsterInfo? monster)
        {
            monster = null;
            return false;
        }

        public IReadOnlyCollection<MonsterInfo> GetAll()
        {
            return Array.Empty<MonsterInfo>();
        }
    }
}
