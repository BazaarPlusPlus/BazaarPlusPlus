using System.Reflection;
using System.Security.Cryptography;
using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.CombatReplay;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Settings;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

// MelonInfo is generated from VERSION by the project file. Patch classes install one at a time
// under the startup mode, never through MelonLoader's assembly-wide PatchAll.
[assembly: HarmonyDontPatchAll]

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The Android host entry: LemonLoader loads this MelonMod from <c>MelonLoader/Mods/</c>. Startup
/// installs the BppLog pipeline over MelonLogger, reads the settings file, logs what a device run
/// must establish (the loaded DLL, the Data Root candidates, the build-channel probe), starts the
/// record chain on the Data Root, then applies the patches and, in the enabled mode, prepares the
/// interaction features: the touch UI, History with its saved-replay runtime, the tooltip
/// previews, and the main menu's release check. Every frame it first runs the main-thread pump
/// (<see cref="AndroidMainThread"/>), where History's and the replay's continuations resume.
/// Quitting tears History and the replay down before the chain stops, which settles a pending run
/// completion and drains the queued writes.
/// </summary>
public sealed class AndroidMod : MelonMod
{
    private const string SettingsFileName = "BazaarPlusPlus.cfg";
    private const string MainMenuSceneName = "MainMenuScene";

    // The hooks summary is checked once a minute and written only when a count changed since the
    // last one written. A scene load brings the next check forward and writes it even unchanged,
    // so a device run always finds a summary after the scene it waits for.
    private const long SummaryIntervalMs = 60_000;
    private const long SceneSummaryDelayMs = 15_000;
    private long _nextSummaryAt;
    private bool _summaryForced;
    private BppLogField[]? _lastSummary;
    private readonly Action _tickCombatStatus;
    private AndroidComposition? _composition;
    private AndroidTouchUi? _touchUi;
    private Game.AndroidCombatStatus? _combatStatus;
    private AndroidCombatStatusView? _combatStatusView;
    private AndroidVersionCheck? _versionCheck;
    private AndroidApkUpdatePrompt? _apkPrompt;
    private AndroidCombatReplayRuntime? _replayRuntime;
    private AndroidHistoryPanel? _historyPanel;
    private AndroidOverlayPanels? _overlayPanels;
    private readonly AndroidCardTemplateIndex _templates = new();
    private bool _mainThreadBound;
    private int _labelRevision;

    public AndroidMod()
    {
        // Cached once: a lambda over this would allocate a delegate every frame.
        _tickCombatStatus = () => _combatStatusView?.Tick();
    }

    public override void OnInitializeMelon()
    {
        AndroidMainThread.Host.Capture();
        BppLog.Install(new MelonLogSink(LoggerInstance));
        var version = Info.Version;
        var settings = OpenSettings();
        var mode = ReadPatchMode(settings);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.plugin.loaded"),
            ("version", version),
            ("mode", mode),
            ("sha256", AssemblySha256()),
            ("game_version", Application.version)
        );
        LogPathProbe();
        var serverOption = LogBuildChannelProbe();
        _composition = StartRecordChain(serverOption, settings);
        // In every mode: a scene change releases the rented cards through it, open panels or not.
        _overlayPanels = new AndroidOverlayPanels(
            static () => Il2CppTheBazaar.Data.IsInCombat,
            AndroidCardRental.ReleaseAll,
            static () => Time.frameCount
        );
        AndroidPatchHost.OverlayPanels = _overlayPanels;
        AndroidPatchHost.Install(HarmonyInstance, mode, version, _composition);
        if (mode == AndroidPatchMode.Enabled)
            StartInteraction(settings);
        _nextSummaryAt = Environment.TickCount64 + SummaryIntervalMs;
    }

    public override void OnUpdate()
    {
        BindMainThread();
        AndroidMainThread.Host.Tick();
        _composition?.Tick(Time.unscaledTime);
        Guarded("screen_fill", AndroidScreenFill.Tick);
        _replayRuntime?.Tick();
        _overlayPanels?.Frame(Time.unscaledDeltaTime);
        _touchUi?.EnsureMounted();
        Guarded("gameplay_previews", () => AndroidPatchHost.GameplayPreviews?.Tick());
        Guarded("combat_status", _tickCombatStatus);
        Guarded("quest_preview", () => AndroidPatchHost.QuestPreview?.Tick());
        Guarded("encounter_preview", () => AndroidPatchHost.EncounterPreview?.Tick());
        AndroidGameFont.Tick();
        AndroidNativeSettingsSection.Tick(Time.unscaledTime);
        AndroidCardRental.Tick();
        _versionCheck?.Tick();
        Guarded("apk_prompt", () => _apkPrompt?.Tick());
        var revision = MainMenuVersionUpdateState.Current.Revision;
        if (revision != _labelRevision)
        {
            _labelRevision = revision;
            VersionLabelPatch.RefreshCurrent();
        }
        if (Environment.TickCount64 < _nextSummaryAt)
            return;
        _nextSummaryAt = Environment.TickCount64 + SummaryIntervalMs;
        BppLogField[] summary = [.. AndroidPatchHost.Summary(), .. InteractionSummary()];
        if (!_summaryForced && SameSummary(summary, _lastSummary))
            return;
        _summaryForced = false;
        _lastSummary = summary;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.hooks.summary"),
            summary
        );
    }

    private static bool SameSummary(BppLogField[] summary, BppLogField[]? last)
    {
        if (last == null || last.Length != summary.Length)
            return false;
        for (var i = 0; i < summary.Length; i++)
            if (summary[i].Name != last[i].Name || !Equals(summary[i].Value, last[i].Value))
                return false;
        return true;
    }

    // History's sessions and the replay's lease, which a device run checks return to zero.
    private BppLogField[] InteractionSummary() =>
        [
            .. _historyPanel?.Summary() ?? [],
            ("replay_starts", _replayRuntime?.Starts ?? 0),
            ("replay_start_failures", _replayRuntime?.StartFailures ?? 0),
            ("replay_exits", _replayRuntime?.ExitsHandled ?? 0),
            ("replay_menu_returns", _replayRuntime?.MenuReturns ?? 0),
            ("replay_session", _replayRuntime?.SavedReplays.HasSavedReplaySession ?? false),
            ("payload_leases_held", _replayRuntime?.SavedReplays.HoldsPayloadLease == true ? 1 : 0),
            ("main_thread_pending", AndroidMainThread.Host.PendingCallbacks),
            ("main_thread_waits", AndroidMainThread.Host.PendingWaits),
            ("main_thread_failures", AndroidMainThread.Host.CallbackFailures),
            .. _touchUi?.Summary() ?? [],
            .. AndroidNativeSettingsSection.Summary(),
        ];

    // OnInitializeMelon bound the pump; the first frame confirms Unity's main thread is that one.
    private void BindMainThread()
    {
        if (_mainThreadBound)
            return;
        _mainThreadBound = true;
        AndroidAppLifecycle.PauseChanged = OnPauseChanged;
        AndroidAppLifecycle.Mount();
        var initializedOn = AndroidMainThread.Host.Capture();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.main_thread.bound"),
            ("thread_id", Environment.CurrentManagedThreadId),
            ("same_as_initialize", initializedOn == Environment.CurrentManagedThreadId)
        );
    }

    private void OnPauseChanged(bool paused) =>
        Guarded(
            paused ? "app_lifecycle:pause" : "app_lifecycle:resume",
            () =>
            {
                if (paused)
                {
                    _touchUi?.OnAppPaused();
                    _composition?.Pause();
                }
                else
                    _composition?.Resume();
            }
        );

    public override void OnApplicationQuit() => Stop();

    public override void OnDeinitializeMelon() => Stop();

    private void Stop()
    {
        AndroidAppLifecycle.PauseChanged = null;
        AndroidPatchHost.GameplayPreviews?.Dispose();
        AndroidPatchHost.GameplayPreviews = null;
        AndroidPatchHost.QuestPreview?.Dispose();
        AndroidPatchHost.QuestPreview = null;
        AndroidPatchHost.AggregateItemPreview = null;
        AndroidPatchHost.EncounterPreview = null;
        _combatStatusView?.Dispose();
        _combatStatusView = null;
        _combatStatus?.Dispose();
        _combatStatus = null;
        AndroidPatchHost.CombatStatus = null;
        AndroidPatchHost.ApkUpdatePrompt = null;
        _apkPrompt = null;
        _versionCheck?.Dispose();
        _versionCheck = null;
        // The touch panels give their rented cards back and drop their views before the rental
        // and the composition's catalogs go.
        _touchUi?.Dispose();
        _touchUi = null;
        // History's coordinator cancels its session before the replay and the composition, and
        // the composition disposes History's Mod API session last.
        _historyPanel?.Dispose();
        _historyPanel = null;
        _replayRuntime?.Dispose();
        _replayRuntime = null;
        AndroidPatchHost.OverlayPanels = null;
        _overlayPanels = null;
        AndroidGameFont.Dispose();
        AndroidMainThread.Host.Stop();
        StopRecordChain();
    }

    // The interaction features read the shared settings the record chain bound, or bind their
    // own when the chain did not start. A failure here leaves the hooks and the chain running.
    private void StartInteraction(IniSettingsStore? settings)
    {
        if (settings == null || _overlayPanels is not { } panels)
            return;
        try
        {
            var shared = _composition?.Settings ?? new BppSettings(settings);
            var android = new AndroidSettings(settings);
            AndroidPatchHost.GameplayPreviews = new AndroidGameplayPreviews(panels);
            panels.SetTooltipPreview(AndroidPatchHost.GameplayPreviews.Collapse);
            AndroidPatchHost.QuestPreview = new Game.AndroidQuestPreview(shared);
            AndroidPatchHost.AggregateItemPreview = new Game.AndroidAggregateItemPreview(shared);
            AndroidPatchHost.EncounterPreview = new Game.AndroidEncounterPreview(shared);
            _combatStatus = new Game.AndroidCombatStatus(shared);
            AndroidPatchHost.CombatStatus = _combatStatus;
            _combatStatusView = new AndroidCombatStatusView(_combatStatus, panels, android);
            L.Install(
                ReadGameLanguageCode,
                () => ChineseScriptConverter.NormalizeMode(shared.ChineseLocaleModeConfig.Value)
            );
            AndroidUpgradePreview.IsEnabled = () => android.UpgradePreview.Value;
            var melonBase = MelonEnvironment.MelonBaseDirectory;
            var devManifest = AndroidVersionCheck.DevManifest(android.DevReleaseManifestPort.Value);
            _versionCheck = new AndroidVersionCheck(
                AndroidPaths.InstallRoot(melonBase),
                AndroidPaths.DataRoot(melonBase),
                Application.version ?? "",
                devManifest
            );
            _apkPrompt = new AndroidApkUpdatePrompt(
                _versionCheck,
                AndroidPaths.DataRoot(melonBase),
                OpenApkUpdate,
                () => panels.OpenPanelId != null
            );
            AndroidPatchHost.ApkUpdatePrompt = _apkPrompt;
            if (devManifest != null)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.version_check.dev_manifest"
                    ),
                    ("manifest", devManifest)
                );
            StartHistory(panels);
            _touchUi = new AndroidTouchUi(
                panels,
                _historyPanel == null ? null : source => _historyPanel?.Open(source),
                _composition?.LiveBuild,
                _composition?.Supporters,
                new AndroidCollectionCatalog(
                    Path.Combine(AndroidPaths.DataRoot(melonBase), "CollectionCatalog")
                )
            );
            var versionCheck = _versionCheck;
            AndroidSettingsDock.Install(
                shared,
                android,
                new AndroidSettingsDockHooks(
                    ReleaseUpgradePreview: AndroidUpgradePreview.Release,
                    HistoryAvailable: () => _historyPanel != null,
                    OpenHistory: () => _historyPanel?.Open("settings"),
                    GameUpdateStatus: () => versionCheck.GameUpdateStatus,
                    OpenGameUpdate: () =>
                    {
                        if (versionCheck.ApkUpdate is { } update)
                            OpenApkUpdate(update);
                    },
                    RefreshPreviews: () => Guarded("preview_settings_refresh", RefreshNativePreview)
                )
            );
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.interaction.start_failed"),
                exception
            );
        }
    }

    // History and its replay runtime need the record chain's database and payload gate; without
    // the chain the launcher offers no History.
    private void StartHistory(AndroidOverlayPanels panels)
    {
        var composition = _composition;
        if (composition == null)
            return;
        try
        {
            var runtime = new AndroidCombatReplayRuntime(
                composition.Services,
                composition.RunLifecycle,
                composition.ReplayGate,
                new SavedReplayCatalog(composition.Battles, composition.Replays)
            );
            runtime.Start();
            _replayRuntime = runtime;
            _historyPanel = new AndroidHistoryPanel(
                composition.CreateHistoryDependencies(
                    () => _replayRuntime?.SavedReplays,
                    _templates.Contains
                ),
                () => _replayRuntime?.SavedReplays,
                _templates,
                () => AndroidGameFont.Font,
                panels,
                composition.Supporters
            );
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.composed"),
                ("ghost_sync", true)
            );
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.start_failed"),
                exception
            );
            _historyPanel?.Dispose();
            _historyPanel = null;
            _replayRuntime?.Dispose();
            _replayRuntime = null;
        }
    }

    private static void OpenApkUpdate(AndroidApkUpdate update)
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.game_update_opened"),
            ("game_version", update.Apk.GameVersion),
            ("reason", update.Reason)
        );
        Application.OpenURL(update.Apk.Url.ToString());
    }

    private static string ReadGameLanguageCode()
    {
        try
        {
            return Il2Cpp.PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void StopRecordChain()
    {
        var composition = _composition;
        _composition = null;
        composition?.Stop();
    }

    // The record chain writes the Data Root's history database, then seals and uploads its runs.
    // A failure here, an unreadable settings file included, leaves the hooks counting with no
    // chain behind them; it never stops the melon.
    private static AndroidComposition? StartRecordChain(
        bool? hasServerOption,
        IniSettingsStore? settings
    )
    {
        try
        {
            if (settings == null)
                throw new InvalidOperationException("The settings file could not be read.");
            // The Payload's version file is Mods/BazaarPlusPlus.version, named after the product.
            var location = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location))
                BppPluginVersion.Initialize(
                    Path.Combine(Path.GetDirectoryName(location)!, "BazaarPlusPlus.dll")
                );
            var gameBuild = GameBuildInfoResolver.Resolve(
                Application.version ?? "",
                hasServerOption
            );
            var dataRoot = AndroidPaths.DataRoot(MelonEnvironment.MelonBaseDirectory);
            var composition = new AndroidComposition(dataRoot, gameBuild, settings);
            composition.Start(Time.unscaledTime);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.record_chain.started"),
                ("data_root", dataRoot),
                ("database_path", composition.DatabasePath),
                ("journal_mode", AndroidComposition.DatabaseOptions.JournalMode),
                ("build_channel", gameBuild.Channel),
                ("mod_version", BppPluginVersion.Current)
            );
            return composition;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.record_chain.start_failed"),
                exception
            );
            return null;
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.scene.loaded"),
            ("index", buildIndex),
            ("name", sceneName)
        );
        _summaryForced = true;
        _nextSummaryAt = Math.Min(_nextSummaryAt, Environment.TickCount64 + SceneSummaryDelayMs);
        // Each step runs in its own guard, so one that throws never skips the next. The Overlay
        // Panel Host closes the panels the scene policies close and tells every panel, so they
        // let go of their leases, then releases whatever is left: no rented card outlives the
        // scene it was rented in.
        Guarded(
            "scene_loaded:upgrade_preview",
            () => AndroidUpgradePreview.Release("scene_changed")
        );
        Guarded(
            "scene_loaded:gameplay_previews",
            () => AndroidPatchHost.GameplayPreviews?.Clear("scene_changed")
        );
        Guarded(
            "scene_loaded:combat_status",
            () =>
            {
                _combatStatus?.Reset();
                AndroidPatchHost.QuestPreview?.Dispose();
                _combatStatusView?.Dispose();
            }
        );
        Guarded("scene_loaded:overlay_panels", () => _overlayPanels?.SceneLoaded());
        if (sceneName == MainMenuSceneName)
            Guarded("scene_loaded:version_check", () => _versionCheck?.Begin());
    }

    private static void RefreshNativePreview()
    {
        AndroidPatchHost.QuestPreview?.Tick();
        var controller = Il2CppTheBazaar.Data.TooltipParentComponent?.CardTooltipController;
        var tooltip =
            controller?.CurrentTooltipData?.TryCast<Il2CppTheBazaar.Tooltips.CardTooltipData>();
        if (controller == null || tooltip == null)
            return;
        controller.ApplyPreviewTooltip(tooltip);
        if (!controller.isLocked)
            controller._legendRenderer?.OnTooltipShown();
    }

    private static void Guarded(string probe, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError(probe, exception);
        }
    }

    private static IniSettingsStore? OpenSettings()
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, SettingsFileName);
        try
        {
            return IniSettingsStore.Open(path);
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.settings.load_failed"),
                exception,
                ("path", path)
            );
            return null;
        }
    }

    private static AndroidPatchMode ReadPatchMode(IniSettingsStore? settings)
    {
        try
        {
            return settings
                    ?.Bind(
                        "Android",
                        "PatchMode",
                        AndroidPatchMode.Enabled,
                        "Enabled installs every hook; Observe installs them but changes nothing; "
                            + "Disabled installs none. Read at startup."
                    )
                    .Value ?? AndroidPatchMode.Enabled;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.settings.load_failed"),
                exception
            );
            return AndroidPatchMode.Enabled;
        }
    }

    private static string AssemblySha256()
    {
        try
        {
            var location = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(location))
                return "unknown";
            using var stream = File.OpenRead(location);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    private static void LogPathProbe()
    {
        try
        {
            var melonBase = MelonEnvironment.MelonBaseDirectory;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.paths.probe"),
                ("melon_base", melonBase),
                ("data_root", AndroidPaths.DataRoot(melonBase)),
                ("persistent_data_path", Application.persistentDataPath)
            );
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.paths.probe_failed"),
                exception
            );
        }
    }

    // GameBuildInfoResolver on desktop corroborates the channel with this nested type, which only
    // the PTR fork of Config declares. The android-online Interop Snapshot has no ServerOption, so
    // an Online build logs "missing", the Online answer; whether Il2CppInterop surfaces the type
    // stays open until a PTR or staging Android build is captured.
    private static bool? LogBuildChannelProbe()
    {
        try
        {
            var serverOption = AccessTools.Inner(typeof(Il2CppTheBazaar.Config), "ServerOption");
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.build_channel.probe"),
                ("game_version", Application.version),
                ("server_option", serverOption != null ? "found" : "missing")
            );
            return serverOption != null;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.build_channel.probe_failed"),
                exception
            );
            return null;
        }
    }
}
