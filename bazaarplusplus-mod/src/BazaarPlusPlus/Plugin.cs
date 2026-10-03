#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.Localization;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Patches;
using BazaarPlusPlus.Patches.Tooltips;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.Sqlite;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    private BppComposition? _composition;
    private ModApiSession? _modApiSession;
    private BazaarDbLinkClient? _bazaarDbLinkClient;
    private bool _patchesApplied;
    private bool _teardownStarted;

    // Captured at startup because teardown releases the database after the
    // composition that owns the path provider is already disposed.
    private string? _runLogDatabasePath;

    protected virtual void Awake()
    {
        BppLog.Install(Logger);
        NativeGameTypography.InitializeForCurrentThread();
        var phase = PluginInitializationPhase.PluginVersion;
        try
        {
            BppPluginVersion.Initialize(Info.Location);

            var configFile = CreatePluginConfigFile();

            phase = PluginInitializationPhase.Composition;
            var gameBuild = GameBuildInfoResolver.Resolve();
            _composition = new BppComposition(configFile, gameBuild);

            var services = _composition.Services;
            var dataRoot = services.Paths.DataRootDirectoryPath;
            _runLogDatabasePath = string.IsNullOrWhiteSpace(dataRoot)
                ? null
                : PathConstants.RunLogDatabase(dataRoot!);
            BppPatchHost.Install(services, _composition.PatchFeatures);

            if (gameBuild.DetectionWarning != null)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Plugin,
                        "plugin.game_build.degraded",
                        storm: ["reason_code"]
                    ),
                    ("game_build", gameBuild.RawVersion),
                    ("build_channel", gameBuild.Channel),
                    (
                        "reason_code",
                        string.IsNullOrWhiteSpace(gameBuild.RawVersion)
                            ? PluginLogReasonCode.VersionUnreadable
                            : PluginLogReasonCode.DetectionSignalsDisagree
                    )
                );
            }

            phase = PluginInitializationPhase.StaticUtilities;
            InstallStaticUtilities(services, _composition.SettingsDockRegistry);

            phase = PluginInitializationPhase.HarmonyPatches;
            ApplyHarmonyPatches();
            NativeTooltipSuppression.CapturePatchCapabilities();

            phase = PluginInitializationPhase.ReplayRuntime;
            // CombatReplayRuntime is constructed before composition.Start() because RunLifecycle
            // and several features take a reference through CombatReplayModule. Not a mountable.
            var combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
            combatReplayRuntime.Initialize(
                services,
                _composition.RunLifecycle,
                _composition.PvpBattleCatalog,
                () => gameObject.GetComponent<Game.CombatReplay.Video.CombatReplayVideoRecorder>()
            );
            _composition.AttachCombatReplayRuntime(combatReplayRuntime);

            phase = PluginInitializationPhase.Features;
            _composition.Start();

            phase = PluginInitializationPhase.OnlineServices;
            BuildOnlineServices();
            _composition.AttachModApiSession(_modApiSession);
            _composition.AttachAccountLinkClient(_bazaarDbLinkClient);

            phase = PluginInitializationPhase.Mountables;
            _composition.Mountables.MountAll(gameObject, services);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.initialization.succeeded"),
                ("plugin_version", MyPluginInfo.PLUGIN_VERSION),
                ("game_build", gameBuild.RawVersion),
                ("build_channel", gameBuild.Channel)
            );
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.initialization.failed"),
                ex,
                ("phase", phase),
                ("reason_code", PluginLogReasonCode.InitializationException)
            );
            CleanupFailedInitialization();
            throw;
        }
    }

    protected virtual void OnDestroy()
    {
        Teardown();
    }

    private void Teardown()
    {
        if (_teardownStarted)
            return;
        _teardownStarted = true;

        var failures = new PluginTeardownAccumulator();
        RunTeardownSteps(failures);
        failures.Run(PluginTeardownStep.ResetPatchHost, BppPatchHost.Reset);
        if (failures.FailedStepCount > 0)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Plugin,
                    "plugin.shutdown.degraded",
                    storm: ["reason_code"]
                ),
                failures.FirstException!,
                ("failed_step_count", failures.FailedStepCount),
                ("first_failed_step", failures.FirstFailedStep),
                ("reason_code", PluginLogReasonCode.TeardownStepFailed)
            );
        }
        BppLog.Flush();
    }

    // Every step runs in isolation and Harmony is unpatched first: a throw in any single
    // step must never strand the "patches applied but static utilities uninstalled"
    // zombie state (patched game code rebuilding UI from reset catalogs).
    private void RunTeardownSteps(PluginTeardownAccumulator failures)
    {
        failures.Run(PluginTeardownStep.UnpatchHarmony, UnpatchHarmony);
        failures.Run(
            PluginTeardownStep.UnmountComponents,
            () => _composition?.Mountables.UnmountAll(gameObject)
        );
        failures.Run(
            PluginTeardownStep.DisposeComposition,
            () =>
            {
                _composition?.Dispose();
                _composition = null;
            }
        );
        failures.Run(
            PluginTeardownStep.DestroyCombatReplayRuntime,
            DestroyComponentIfPresent<CombatReplayRuntime>
        );
        failures.Run(PluginTeardownStep.DisposeOnlineServices, DisposeOnlineServices);
        // After every writer is gone, so the checkpoint can reclaim the whole WAL.
        failures.Run(
            PluginTeardownStep.ReleaseRunLogDatabase,
            () => SqliteShutdown.ReleaseRunLogDatabase(_runLogDatabasePath)
        );
        failures.Run(PluginTeardownStep.UninstallStaticUtilities, UninstallStaticUtilities);
    }

    private ConfigFile CreatePluginConfigFile()
    {
        return new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
    }

    private static void InstallStaticUtilities(
        IBppServices services,
        SettingsDockEntryRegistry settingsDockRegistry
    )
    {
        LegendaryPositionDisplayFormatter.Install(services.Config);
        var config = services.Config;
        L.Install(
            ReadGameLanguageCode,
            () => ChineseScriptConverter.NormalizeMode(config.ChineseLocaleModeConfig.Value)
        );
        var attributeUnitLocalizer = BppTooltipText.TryLocalizeKeyword;
        CollectionLocalizationResolver.AttributeUnitLocalizer = attributeUnitLocalizer;
        EventPreviewLocalization.AttributeUnitLocalizer = attributeUnitLocalizer;
        BppSettingsDockCatalog.Install(services.Config, settingsDockRegistry);
        BPPSupporterCatalog.Install(services.Config);
        BppHotkeyService.Install(services.Config);
    }

    private static string ReadGameLanguageCode()
    {
        try
        {
            return PlayerPreferences.Data.LanguageCode ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void UninstallStaticUtilities()
    {
        LegendaryPositionDisplayFormatter.Reset();
        L.Reset();
        CollectionLocalizationResolver.AttributeUnitLocalizer = null;
        EventPreviewLocalization.AttributeUnitLocalizer = null;
        BppSettingsDockCatalog.Reset();
        NativeSettingsLogState.Reset();
        BPPSupporterCatalog.Reset();
        BppHotkeyService.Reset();
        TooltipEncounterProbeReader.Reset();
        BppTooltipSectionRenderPatch.ResetEncounterHealth();
        ChineseTranslationCatalog.Reset();
        NativeGameTypography.Reset();
    }

    private void BuildOnlineServices()
    {
        // BazaarDB account linking targets a different host (bazaardb.gg) and is independent of the
        // mod API base URL, so build it regardless of mod-api routes validity. Its dedicated bare
        // HttpClient carries no mod-api auth/base address; a 30s timeout keeps a hung redeem from
        // stalling the link card for the HttpClient default of ~100s.
        var linkHttpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "BazaarDbLink",
            timeout: TimeSpan.FromSeconds(30)
        );
        _bazaarDbLinkClient = new BazaarDbLinkClient(
            linkHttpClient,
            new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint)
        );

        // The base URL is a constant, so a null session is a build defect: fail startup.
        _modApiSession =
            ModApiSession.TryCreate(
                ModApiUploadDefaults.ApiBaseUrl,
                BppPluginVersion.Current,
                "OnlineClient",
                TimeSpan.FromSeconds(ModApiUploadDefaults.RequestTimeoutSeconds)
            )
            ?? throw new InvalidOperationException(
                $"Mod API base URL '{ModApiUploadDefaults.ApiBaseUrl}' is not an absolute URL."
            );
    }

    private void ApplyHarmonyPatches()
    {
        // Patch classes are applied one by one instead of PatchAll(): PatchAll aborts at
        // the first failing class, leaving earlier classes applied and later ones not —
        // one broken game target (e.g. after a game update or on the PTR branch) must
        // degrade only its own feature, never the whole plugin. The flag is set before
        // the loop so a partial application is always unpatched during teardown.
        _patchesApplied = true;
        var failedClasses = 0;
        // GetTypesFromAssembly (not Assembly.GetTypes) tolerates types that fail to load;
        // CreateClassProcessor(type).Patch() is a no-op for non-patch types, so this
        // covers exactly PatchAll's discovery set.
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            try
            {
                _harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                failedClasses++;
                BppLog.DebugEvent(
                    new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.patch.apply_failed"),
                    ex,
                    () =>
                        [
                            ("patch_type", type.FullName ?? type.Name),
                            ("reason_code", PluginLogReasonCode.PatchClassException),
                        ]
                );
            }
        }

        if (failedClasses > 0)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Plugin,
                    "plugin.patches.degraded",
                    storm: ["reason_code"]
                ),
                ("failed_patch_count", failedClasses),
                ("reason_code", PluginLogReasonCode.PatchClassesFailed)
            );
    }

    private void CleanupFailedInitialization()
    {
        Teardown();
    }

    private void DisposeOnlineServices()
    {
        _bazaarDbLinkClient?.Dispose();
        _bazaarDbLinkClient = null;
        _modApiSession?.Dispose();
        _modApiSession = null;
    }

    private void UnpatchHarmony()
    {
        if (!_patchesApplied)
            return;

        _harmony.UnpatchSelf();
        NativeTooltipSuppression.ClearPatchCapabilities();
        _patchesApplied = false;
    }

    private void DestroyComponentIfPresent<T>()
        where T : Component
    {
        var component = GetComponent<T>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }
}
