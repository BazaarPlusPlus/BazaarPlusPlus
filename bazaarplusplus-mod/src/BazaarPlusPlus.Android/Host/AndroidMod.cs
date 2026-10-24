using System.Reflection;
using System.Security.Cryptography;
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
    private const long SummaryIntervalMs = 15_000;
    private long _nextSummaryAt;
    private AndroidComposition? _composition;
    private AndroidTouchUi? _touchUi;
    private AndroidVersionCheck? _versionCheck;
    private AndroidCombatReplayRuntime? _replayRuntime;
    private AndroidHistoryPanel? _historyPanel;
    private readonly AndroidCardTemplateIndex _templates = new();
    private bool _mainThreadBound;
    private int _labelRevision;

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
        _replayRuntime?.Tick();
        _historyPanel?.Tick();
        _touchUi?.EnsureMounted();
        AndroidNativeSettingsSection.Tick(Time.unscaledTime);
        AndroidCardRental.Tick();
        _versionCheck?.Tick();
        var revision = MainMenuVersionUpdateState.Current.Revision;
        if (revision != _labelRevision)
        {
            _labelRevision = revision;
            VersionLabelPatch.RefreshCurrent();
        }
        if (Environment.TickCount64 < _nextSummaryAt)
            return;
        _nextSummaryAt = Environment.TickCount64 + SummaryIntervalMs;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.hooks.summary"),
            [.. AndroidPatchHost.Summary(), .. InteractionSummary()]
        );
    }

    // History's sessions and the replay's lease, which a device run checks return to zero.
    private BppLogField[] InteractionSummary() =>
        [
            .. _historyPanel?.Summary() ?? [],
            ("replay_starts", _replayRuntime?.Starts ?? 0),
            ("replay_start_failures", _replayRuntime?.StartFailures ?? 0),
            ("replay_exits", _replayRuntime?.ExitsHandled ?? 0),
            ("replay_menu_returns", _replayRuntime?.MenuReturns ?? 0),
            ("replay_session", _replayRuntime?.HasSavedReplaySession ?? false),
            ("payload_leases_held", _replayRuntime?.PayloadLeasesHeld ?? 0),
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
        var initializedOn = AndroidMainThread.Host.Capture();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.main_thread.bound"),
            ("thread_id", Environment.CurrentManagedThreadId),
            ("same_as_initialize", initializedOn == Environment.CurrentManagedThreadId)
        );
    }

    public override void OnApplicationQuit() => Stop();

    public override void OnDeinitializeMelon() => Stop();

    private void Stop()
    {
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
        AndroidMainThread.Host.Stop();
        StopRecordChain();
    }

    // The interaction features read the shared settings the record chain bound, or bind their
    // own when the chain did not start. A failure here leaves the hooks and the chain running.
    private void StartInteraction(IniSettingsStore? settings)
    {
        if (settings == null)
            return;
        try
        {
            var shared = _composition?.Settings ?? new BppSettings(settings);
            var android = new AndroidSettings(settings);
            L.Install(
                ReadGameLanguageCode,
                () => ChineseScriptConverter.NormalizeMode(shared.ChineseLocaleModeConfig.Value)
            );
            AndroidUpgradePreview.IsEnabled = () => android.UpgradePreview.Value;
            AndroidEnchantPreview.Mode = () => shared.EnchantPreviewModeConfig.Value;
            var melonBase = MelonEnvironment.MelonBaseDirectory;
            var devManifest = AndroidVersionCheck.DevManifest(android.DevReleaseManifestPort.Value);
            _versionCheck = new AndroidVersionCheck(
                AndroidPaths.InstallRoot(melonBase),
                AndroidPaths.DataRoot(melonBase),
                Application.version ?? "",
                devManifest
            );
            if (devManifest != null)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.version_check.dev_manifest"
                    ),
                    ("manifest", devManifest)
                );
            StartHistory();
            var touchUi = new AndroidTouchUi(
                android,
                _historyPanel == null ? null : () => _historyPanel?.Open("launcher"),
                () => _historyPanel?.IsVisible == true,
                _composition?.LiveBuild
            );
            _touchUi = touchUi;
            var versionCheck = _versionCheck;
            AndroidSettingsDock.Install(
                shared,
                android,
                new AndroidSettingsDockHooks(
                    CloseCollection: touchUi.CloseCollection,
                    CloseLiveBuild: touchUi.CloseLiveBuild,
                    CloseHistory: reason => _historyPanel?.Close(reason),
                    ReleaseUpgradePreview: AndroidUpgradePreview.Release,
                    ReleaseRentedCards: AndroidCardRental.ReleaseAll,
                    HistoryAvailable: () => _historyPanel != null,
                    OpenHistory: () => _historyPanel?.Open("settings"),
                    GameUpdateStatus: () => versionCheck.GameUpdateStatus,
                    OpenGameUpdate: () => OpenGameUpdate(versionCheck)
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
    private void StartHistory()
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
                new SavedReplayCatalog(composition.Battles, composition.Replays),
                composition.DatabasePath
            );
            runtime.Start();
            _replayRuntime = runtime;
            _historyPanel = new AndroidHistoryPanel(
                composition.CreateHistoryDependencies(() => _replayRuntime, _templates.Contains),
                () => _replayRuntime,
                _templates,
                () => VersionLabelPatch.Font
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

    private static void OpenGameUpdate(AndroidVersionCheck versionCheck)
    {
        var apk = versionCheck.GameUpdate;
        if (apk == null)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.game_update_opened"),
            ("game_version", apk.GameVersion)
        );
        Application.OpenURL(apk.Url.ToString());
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
        // Each step runs in its own guard, so one that throws never skips the release after it.
        // The panels let go of their leases first, then whatever is left is destroyed: no rented
        // card outlives the scene it was rented in.
        Guarded(
            "scene_loaded:upgrade_preview",
            () => AndroidUpgradePreview.Release("scene_changed")
        );
        Guarded("scene_loaded:touch_ui", () => _touchUi?.OnSceneLoaded());
        Guarded("scene_loaded:history", () => _historyPanel?.OnSceneLoaded(sceneName));
        Guarded("scene_loaded:card_rental", () => AndroidCardRental.ReleaseAll("scene_changed"));
        if (sceneName == MainMenuSceneName)
            Guarded("scene_loaded:version_check", () => _versionCheck?.Begin());
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
