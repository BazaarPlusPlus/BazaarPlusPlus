using System.Reflection;
using System.Security.Cryptography;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.History;
using BazaarPlusPlus.Android.Lobby;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
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
/// interaction features: the touch UI, the tooltip previews, and the main menu's release check.
/// Quitting stops the chain, which settles a pending run completion and drains the queued writes.
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
    private int _labelRevision;

    public override void OnInitializeMelon()
    {
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
        _composition?.Tick(Time.unscaledTime);
        _touchUi?.EnsureMounted();
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
            AndroidPatchHost.Summary()
        );
    }

    public override void OnApplicationQuit() => Stop();

    public override void OnDeinitializeMelon() => Stop();

    private void Stop()
    {
        _versionCheck?.Dispose();
        _versionCheck = null;
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
            AndroidEnchantPreview.IsEnabled = () =>
                shared.EnchantPreviewModeConfig.Value == PreviewVisibilityMode.Always;
            var melonBase = MelonEnvironment.MelonBaseDirectory;
            _versionCheck = new AndroidVersionCheck(
                AndroidPaths.InstallRoot(melonBase),
                AndroidPaths.DataRoot(melonBase),
                Application.version ?? ""
            );
            _touchUi = new AndroidTouchUi(
                shared,
                android,
                _versionCheck,
                _composition == null ? null : new AndroidHistoryReader(_composition.DatabasePath)
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
        try
        {
            AndroidUpgradePreview.Release("scene_changed");
            // The panels let go of their leases first, then whatever is left is destroyed:
            // no rented card outlives the scene it was rented in.
            _touchUi?.OnSceneLoaded();
            AndroidCardRental.ReleaseAll("scene_changed");
            if (sceneName == MainMenuSceneName)
                _versionCheck?.Begin();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("scene_loaded", exception);
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
