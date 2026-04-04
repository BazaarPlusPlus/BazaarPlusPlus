#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Upload;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Tooltips;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BazaarPlusPlus;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    private BppRuntimeHost? _runtimeHost;

    protected virtual void Awake()
    {
        try
        {
            BppPluginVersion.Initialize(Info.Location);
            var configFile = CreateConfigFile();
            var runtime = InstallRuntimeHost(configFile);
            var services = runtime.Services;
            var combatReplayRuntime = runtime.CombatReplayRuntime;
            BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");

            BppLog.Info("Plugin", "Applying Harmony patches");
            _harmony.PatchAll();
            BppLog.Info("Plugin", "Harmony patches applied");

            BppLog.Info("Plugin", "Attaching runtime components");
            AttachRuntimeComponents(services, () => combatReplayRuntime);
            BppLog.Info("Plugin", "Runtime components attached");

            BppLog.Info("Plugin", "Plugin components attached");
        }
        catch (Exception ex)
        {
            BppLog.Error("Plugin", "Plugin initialization failed", ex);
            throw;
        }
    }

    protected virtual void OnDestroy()
    {
        _harmony.UnpatchSelf();
        _runtimeHost?.Stop();
        BppLog.Flush();
    }

    private ConfigFile CreateConfigFile()
    {
        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        HistoryPanelPreviewSettings.Initialize(configFile);
        return configFile;
    }

    private (
        BppRuntimeServices Services,
        RunLifecycleModule LifecycleModule,
        CombatReplayRuntime CombatReplayRuntime
    ) InstallRuntimeHost(ConfigFile configFile)
    {
        CombatReplayRuntime? combatReplayRuntime = null;
        _runtimeHost = new BppRuntimeHost(
            gameObject,
            Logger,
            configFile,
            () => combatReplayRuntime
        );
        _runtimeHost.Install();
        combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
        _runtimeHost.Start();
        return (_runtimeHost.Services, _runtimeHost.LifecycleModule, combatReplayRuntime);
    }

    private void AttachRuntimeComponents(
        BppRuntimeServices services,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
    )
    {
        BppLog.Info("Plugin", "Adding RunLoggingController");
        gameObject.AddComponent<RunLoggingController>();
        BppLog.Info("Plugin", "Adding RunUploadController");
        gameObject.AddComponent<RunUploadController>();
        BppLog.Info("Plugin", "Adding BattleUploadController");
        gameObject.AddComponent<BattleUploadController>();
        BppLog.Info("Plugin", "Adding HistoryPanel");
        var historyPanel = gameObject.AddComponent<HistoryPanel>();
        var historyPanelRuntime = new HistoryPanelRuntime(
            services.RunContext,
            services.Paths.RunLogDatabasePath,
            services.Paths.CombatReplayDirectoryPath,
            combatReplayRuntimeAccessor
        );
        historyPanel.Configure(HistoryPanelFactory.Create(historyPanelRuntime));
        BppLog.Info("Plugin", "Adding CombatStatusBar");
        gameObject.AddComponent<CombatStatusBar>();
        BppLog.Info("Plugin", "Adding MonsterPreviewController");
        gameObject.AddComponent<MonsterPreviewController>();
        BppLog.Info("Plugin", "Adding MonsterPreviewWarmupController");
        gameObject.AddComponent<MonsterPreviewWarmupController>();
        BppLog.Info("Plugin", "Adding MonsterLockShowcaseRuntime");
        gameObject.AddComponent<MonsterLockShowcaseRuntime>();

        BppLog.Info("Plugin", "Adding TooltipModifierRefreshController");
        var tooltipModifierRefreshController =
            gameObject.AddComponent<TooltipModifierRefreshController>();
        tooltipModifierRefreshController.Initialize(services.Config);
        BppLog.Info("Plugin", "TooltipModifierRefreshController initialized");
    }
}
