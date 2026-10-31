#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.CosmeticNames;
using BazaarPlusPlus.Game.DetailedPreview;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.GraphicsUpscaling;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Game.MusicNotes;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Game.PostCombatImpact;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.Encounter;
using BazaarPlusPlus.GameInterop.RunLifecycle;
using BazaarPlusPlus.GameInterop.RunSnapshot;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.Mobile;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Patches;
using BazaarPlusPlus.Patches.PostCombatImpact;
using BazaarPlusPlus.Patches.Tooltips;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus;

/// <summary>
/// The Mono host composition root (desktop BepInEx, iOS port): binds the host ports and Mono probes into
/// <see cref="CoreComposition"/>, then wires the feature modules, settings dock rows, and Unity
/// mountables that still live in this host.
/// </summary>
internal sealed class MonoHostComposition : IDisposable
{
    private readonly CoreComposition _core;
    private readonly BppStaticCardMapProvider _staticCardMapProvider = new();
    private readonly GameDataDayTierResolver _dayTierResolver = new GameDataDayTierResolver(
        new GameDataDayTierSource()
    );
    private readonly IBppServices _services;
    private readonly BppMountableRegistry _mountables = new();
    private readonly SettingsDockEntryRegistry _settingsDockRegistry = new();
    private readonly RunLifecycleModule _runLifecycle;
    private readonly CombatReplayModule _combatReplayModule;
    private readonly CombatStatusBarModule _combatStatusBarModule;
    private readonly PostCombatImpactModule _postCombatImpactModule;
    private readonly RunLoggingModule _runLoggingModule;
    private readonly BundleSealCoordinator _bundleSealCoordinator;
    private readonly EncounterPreviewModule _encounterPreviewModule;
    private readonly INativeCardPreviewHost _nativeCardPreviewHost;
    private readonly EndOfRunCaptureWorkflow _endOfRunCaptureWorkflow;
    private readonly BppPatchFeatures _patchFeatures;
    private readonly VoiceSubtitlesModule _voiceSubtitlesModule;
    private readonly VoiceSubtitlesInteropModule _voiceSubtitlesInteropModule;
    private readonly SupporterCatalogModule _supporterCatalogModule;
    private readonly IRemoteEmbeddedCatalog<TenWinBuildCorpus> _buildRecommendationCatalog;
    private readonly BuildRecommendationRepository _buildRecommendationRepository;
    private ModApiSession? _modApiSessionRef;
    private BazaarDbLinkClient? _accountLinkClientRef;
    private PvpBattleCatalog? _pvpBattleCatalog;

    public IBppServices Services => _services;
    public RunLifecycleModule RunLifecycle => _runLifecycle;

    public IPvpBattleCatalog PvpBattleCatalog =>
        _pvpBattleCatalog ??= new PvpBattleCatalog(
            new PvpBattleSqliteStore(
                PathConstants.RunLogDatabase(_services.Paths.RequireDataRoot()),
                SqliteOpenOptions.Default
            )
        );

    public BppMountableRegistry Mountables => _mountables;
    public SettingsDockEntryRegistry SettingsDockRegistry => _settingsDockRegistry;
    public BppPatchFeatures PatchFeatures => _patchFeatures;

    public MonoHostComposition(
        IBppSettingsStore settingsStore,
        IGameBuildInfo gameBuild,
        IPathProvider paths,
        bool selfUpdate
    )
    {
        if (settingsStore == null)
            throw new ArgumentNullException(nameof(settingsStore));
        if (gameBuild == null)
            throw new ArgumentNullException(nameof(gameBuild));

        if (paths == null)
            throw new ArgumentNullException(nameof(paths));
        _core = new CoreComposition(
            settingsStore,
            paths,
            new GameStateProbe(),
            new EncounterStateProbe(),
            new RunSnapshotProbe(),
            gameBuild
        );
        _services = _core.Services;
        var eventBus = _services.EventBus;

        _runLifecycle = new RunLifecycleModule(
            eventBus,
            _services.GameStateProbe,
            _services.RunContext,
            new GameRunLifecycleSource()
        );
        _combatReplayModule = new CombatReplayModule(eventBus);
        _combatStatusBarModule = new CombatStatusBarModule(eventBus);
        _postCombatImpactModule = new PostCombatImpactModule(_staticCardMapProvider);
        _voiceSubtitlesModule = new VoiceSubtitlesModule(_services.Paths.RequireDataRoot());
        _voiceSubtitlesInteropModule = new VoiceSubtitlesInteropModule();
        var dataRoot = _services.Paths.RequireDataRoot();
        _supporterCatalogModule = new SupporterCatalogModule(
            () => _services.Config.UseFixedSupporterListConfig.Value,
            observer => SupporterCatalogFactory.Create(dataRoot, observer),
            () => DateTime.UtcNow,
            SupporterCatalogSettings.BuildCacheFilePath(dataRoot)
        );
        _nativeCardPreviewHost = new NativeCardPreviewHost(new NativeTooltipDataFactoryAdapter());
        _buildRecommendationCatalog = TenWinBuildCatalogFactory.Create(
            _services.Paths.RequireDataRoot()
        );
        _buildRecommendationRepository = new BuildRecommendationRepository(
            _buildRecommendationCatalog,
            StaticDataCardSizeSource.Resolve
        );
        var encounterPreviewCachePath = System.IO.Path.Combine(
            _services.Paths.RequireDataRoot(),
            "EncounterPreview",
            "preview-plans.json"
        );
        _encounterPreviewModule = new EncounterPreviewModule(
            _services,
            _staticCardMapProvider,
            _dayTierResolver,
            encounterPreviewCachePath
        );
        _endOfRunCaptureWorkflow = new EndOfRunCaptureWorkflow(_services);
        _patchFeatures = new BppPatchFeatures(
            _encounterPreviewModule,
            _endOfRunCaptureWorkflow,
            _postCombatImpactModule
        );
        var battleCatalog = PvpBattleCatalog;
        _runLoggingModule = new RunLoggingModule(
            eventBus,
            _services.RunContext,
            _services.RunSnapshot,
            _services.GameBuild.Channel.ToString(),
            () =>
                new QueuedRunLogStore(
                    new RunLogStore(_services.Paths),
                    new RunLogStoreLoggerBridge()
                ),
            battleCatalog.AttachToRun,
            () => _combatReplayModule.Runtime?.HasPendingPersistence == true,
            static () => DateTimeOffset.UtcNow,
            PathConstants.RunLogDatabase(_services.Paths.RequireDataRoot()),
            playerAccountIdResolver: BppClientCacheBridge.TryGetProfileAccountId,
            bundleScreenshotRequestedResolver: () => _services.Config.BazaarDbUploadEnabled.Value,
            modVersionResolver: () => BppPluginVersion.Current,
            subscribeRankRefreshed: onRefreshed =>
                BppClientCacheBridge.SubscribeRankRefreshed(
                    (rank, rating) => onRefreshed(new RankSnapshot { Rank = rank, Rating = rating })
                )
        );
        _bundleSealCoordinator = DesktopBundleSealCoordinator.Create(_services);

        _core.Register(_runLifecycle);
        _core.Register(_combatReplayModule);
        _core.Register(_combatStatusBarModule);
        _core.Register(_postCombatImpactModule);
        _core.Register(_voiceSubtitlesInteropModule);
        _core.Register(_voiceSubtitlesModule);
        _core.Register(_supporterCatalogModule);
        _core.Register(_runLoggingModule);
        _core.Register(_bundleSealCoordinator);

        _settingsDockRegistry.Register(BazaarDbBundleSettingsDockEntry.Create());
        _settingsDockRegistry.Register(FixedSupporterListSettingsDockEntry.Create());
        VoiceSubtitlesSettingsDockEntry.RegisterAll(_settingsDockRegistry);
        _settingsDockRegistry.Register(ChineseLocaleModeSettingsDockEntry.Create(eventBus));
        _settingsDockRegistry.Register(BilingualItemNamesSettingsDockEntry.Create());
        _settingsDockRegistry.Register(CosmeticNamesSettingsDockEntry.Create());
        _settingsDockRegistry.Register(CombatStatusBarSettingsDockEntry.Create());
        _settingsDockRegistry.Register(new EndOfRunScreenshotSettingsDockEntry());
        _settingsDockRegistry.Register(ItemEnchantPreviewSettingsDockEntry.Create());
        // The Mobile Platform Port registers touch panels with the host this mount adds.
        var overlayPanelHostMount = new OverlayPanelHostMount();
        if (TouchPlatform.IsMobile)
        {
            // Before any touch surface builds Mobile UI (ADR-0025).
            MobilePlatform.Install(new TouchMobilePlatform(() => overlayPanelHostMount.Host));
            // A touch screen has no keyboard: the History row drops its hotkey, the upgrade
            // switch sets where each tooltip's own toggle starts, and the enchant preview mode
            // lists the enchantments in each tooltip's main text, as on Android (ADR-0024).
            TouchUpgradePreview.Bind(settingsStore);
            TouchEnchantPreview.Bind(
                () => _services.Config.EnchantPreviewModeConfig.Value,
                () => TooltipEncounterProbeReader.ReadChoice(_services.EncounterState)
            );
            _settingsDockRegistry.Register(
                HistoryPanelHotkeyDockEntry.Create(resolveHotkeyDisplay: null)
            );
            _settingsDockRegistry.Register(TouchUpgradePreview.CreateDockEntry());
        }
        else
        {
            _settingsDockRegistry.Register(HistoryPanelHotkeyDockEntry.Create());
            _settingsDockRegistry.Register(UpgradePreviewActivationSettingsDockEntry.Create());
        }
        _settingsDockRegistry.Register(
            DetailedPreviewSettingsDockEntry.Create(enabled =>
            {
                if (enabled)
                    return;
                EncounterEventTooltipPatch.ClearPooledPresentation();
                HeroLevelRewardsTooltipPatch.ClearPooledPresentation();
                QuestRewardPreviewTooltipPatch.ClearPooledPresentation();
                AggregateItemMissingTypesTooltipPatch.ClearPooledPresentation();
            })
        );
        _settingsDockRegistry.Register(LegendaryPositionSettingsDockEntry.Create());
        _settingsDockRegistry.Register(NameOverrideSettingsDockEntry.Create());
        if (
            UnityEngine.Application.platform
            is UnityEngine.RuntimePlatform.OSXPlayer
                or UnityEngine.RuntimePlatform.WindowsPlayer
        )
            GraphicsUpscalingSettingsDockEntry.RegisterAll(_settingsDockRegistry);

        _mountables.Register(new UploadPumpMount());
        if (
            UnityEngine.Application.platform
            is UnityEngine.RuntimePlatform.OSXPlayer
                or UnityEngine.RuntimePlatform.WindowsPlayer
        )
        {
            _mountables.Register(
                new ComponentMount<GraphicsUpscalingController>(
                    (controller, services) => controller.Initialize(services.Config)
                )
            );
        }
        _mountables.Register(
            new ComponentMount<EventPreviewStaticDataObserver>(
                (observer, _) => observer.Initialize(_encounterPreviewModule)
            )
        );
        // The overlay host must mount before every Main Overlay Panel mount below: panels
        // register their lifecycle with it through the accessor.
        _mountables.Register(overlayPanelHostMount);
        _mountables.Register(
            new CollectionPanelMount(
                () => overlayPanelHostMount.Host,
                _staticCardMapProvider,
                _nativeCardPreviewHost,
                _dayTierResolver,
                _patchFeatures.CollectionInspection
            )
        );
        _mountables.Register(
            new ComponentMount<CombatReplayVideoRecorder>((c, s) => c.Initialize(s))
        );
        _mountables.Register(new ComponentMount<CombatStatusBar>((c, s) => c.Initialize(s)));
        // A mobile platform (ADR-0024) mounts only what the Android host has: the music note
        // preview waits for a held key and the post-combat impact for a hover and Shift, so
        // neither is reachable by touch; the tooltip toggles take the held key's place.
        if (TouchPlatform.IsMobile)
        {
            _mountables.Register(new ComponentMount<TouchGameplayPreviews>());
            // The game fills a screen narrower than 16:10 instead of letterboxing it (ADR-0025).
            _mountables.Register(new ComponentMount<TouchScreenFill>());
        }
        else
        {
            _mountables.Register(new ComponentMount<NativeMusicNotePreviewController>());
            // Plugin-lifetime, like _nativeCardPreviewHost: composition does not dispose
            // GameInterop hosts, and the releasable unit is the session the view acquires from it.
            var pairedTooltipHost = new NativePairedTooltipHost();
            _mountables.Register(
                new ComponentMount<PostCombatImpactController>(
                    (c, _) =>
                        c.Initialize(
                            _postCombatImpactModule,
                            new NativePostCombatImpactTooltipView(
                                _nativeCardPreviewHost,
                                pairedTooltipHost
                            )
                        )
                )
            );
        }
        _mountables.Register(
            new ComponentMount<EndOfRunCaptureDriver>(
                (driver, services) => driver.Initialize(_endOfRunCaptureWorkflow, services)
            )
        );
        if (selfUpdate)
            _mountables.Register(
                new ComponentMount<MainMenuVersionCheckController>(
                    (c, services) => c.Initialize(services)
                )
            );
        _mountables.Register(
            new HistoryPanelMount(
                combatReplayRuntime: () => _combatReplayModule.Runtime,
                modApiSession: () => _modApiSessionRef,
                accountLinkClient: () => _accountLinkClientRef,
                overlayHost: () => overlayPanelHostMount.Host
            )
        );
        _mountables.Register(
            new LiveBuildPanelMount(
                () => overlayPanelHostMount.Host,
                _buildRecommendationRepository
            )
        );
        _mountables.Register(
            new ComponentMount<CosmeticNameOverlayObserver>(
                (observer, services) => observer.Initialize(services.Config)
            )
        );
        _mountables.Register(new ComponentMount<VoiceLineDisplayDispatcher>());
        _mountables.Register(new ComponentMount<VersionLabelScanner>());
        _mountables.Register(
            new ComponentMount<TooltipModifierRefreshController>(
                (c, s) => c.Initialize(s.Config, s.EncounterState, _nativeCardPreviewHost)
            )
        );
    }

    public void AttachCombatReplayRuntime(CombatReplayRuntime runtime) =>
        _combatReplayModule.AttachRuntime(runtime);

    public void AttachModApiSession(ModApiSession? session) => _modApiSessionRef = session;

    public void AttachAccountLinkClient(BazaarDbLinkClient? client) =>
        _accountLinkClientRef = client;

    public void Start() => _core.Start();

    public void Dispose()
    {
        _core.Stop();
        _endOfRunCaptureWorkflow.Dispose();
        _encounterPreviewModule.Dispose();
        _buildRecommendationCatalog.Dispose();
    }
}
