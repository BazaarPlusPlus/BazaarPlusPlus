#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Patches;
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
    private ModOnlineClient? _onlineClient;
    private bool _patchesApplied;

    protected virtual void Awake()
    {
        try
        {
            BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");
            BppPluginVersion.Initialize(Info.Location);

            var configFile = CreatePluginConfigFile();

            _composition = new BppComposition(Logger, configFile);

            var services = _composition.Services;
            BppLog.Install(services.Logger);
            BppPatchHost.Install(services);

            InstallStaticUtilities(services, _composition.SettingsDockRegistry);

            ApplyHarmonyPatches();

            BppLog.Info("Plugin", "Adding CombatReplayRuntime");
            // CombatReplayRuntime is constructed before composition.Start() because RunLifecycle
            // and several features take a reference through CombatReplayModule. Not a mountable.
            var combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
            combatReplayRuntime.Initialize(
                services,
                _composition.RunLifecycle,
                _composition.PvpBattleCatalog
            );
            _composition.AttachCombatReplayRuntime(combatReplayRuntime);

            _composition.Start();

            BuildOnlineServices();
            _composition.AttachOnlineClient(_onlineClient);

            BppLog.Info("Plugin", "Attaching runtime components");
            _composition.Mountables.MountAll(gameObject, services);
            BppLog.Info("Plugin", "Runtime components attached");

            BppLog.Info("Plugin", "Plugin initialization completed");
        }
        catch (Exception ex)
        {
            BppLog.Error("Plugin", "Plugin initialization failed", ex);
            CleanupFailedInitialization();
            throw;
        }
    }

    protected virtual void OnDestroy()
    {
        try
        {
            _composition?.Mountables.UnmountAll(gameObject);
            DestroyComponentIfPresent<CombatReplayRuntime>();
            _composition?.Dispose();
            _composition = null;
            DisposeOnlineServices();
            UnpatchHarmony();
        }
        finally
        {
            UninstallStaticUtilities();
            BppLog.Flush();
            BppPatchHost.Reset();
        }
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
        L.Install(new GameLanguageProvider(), new ChineseLocaleModeProvider(services.Config));
        BppSettingsDockCatalog.Install(services.Config, settingsDockRegistry);
        BppHotkeyService.Install(services.Config);
        RunLoggingGameDataReader.Install(services.RunContext);
    }

    private static void UninstallStaticUtilities()
    {
        LegendaryPositionDisplayFormatter.Reset();
        L.Reset();
        BppSettingsDockCatalog.Reset();
        BppHotkeyService.Reset();
        RunLoggingGameDataReader.Reset();
    }

    private void BuildOnlineServices()
    {
        var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
        if (routes == null)
        {
            BppLog.Warn("Plugin", "ModApi base URL invalid; online services will be inactive.");
            return;
        }

        var httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "OnlineClient",
            timeout: TimeSpan.FromSeconds(Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds))
        );
        _onlineClient = new ModOnlineClient(httpClient, routes);
        BppLog.Info("Plugin", "Online client ready.");
    }

    private void ApplyHarmonyPatches()
    {
        BppLog.Info("Plugin", "Applying Harmony patches");
        _harmony.PatchAll();
        _patchesApplied = true;
        BppLog.Info("Plugin", "Harmony patches applied");
    }

    private void CleanupFailedInitialization()
    {
        _composition?.Mountables.UnmountAll(gameObject);
        DestroyComponentIfPresent<CombatReplayRuntime>();
        _composition?.Dispose();
        _composition = null;
        DisposeOnlineServices();
        UnpatchHarmony();
        UninstallStaticUtilities();
    }

    private void DisposeOnlineServices()
    {
        _onlineClient?.Dispose();
        _onlineClient = null;
    }

    private void UnpatchHarmony()
    {
        if (!_patchesApplied)
            return;

        _harmony.UnpatchSelf();
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
