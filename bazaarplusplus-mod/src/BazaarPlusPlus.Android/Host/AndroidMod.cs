using System.Reflection;
using System.Security.Cryptography;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
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
/// must establish (the loaded DLL, the Data Root candidates, the build-channel probe), then
/// applies the patches.
/// </summary>
public sealed class AndroidMod : MelonMod
{
    private const string SettingsFileName = "BazaarPlusPlus.cfg";
    private const long SummaryIntervalMs = 15_000;
    private long _nextSummaryAt;

    public override void OnInitializeMelon()
    {
        BppLog.Install(new MelonLogSink(LoggerInstance));
        var version = Info.Version;
        var mode = ReadPatchMode();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.plugin.loaded"),
            ("version", version),
            ("mode", mode),
            ("sha256", AssemblySha256()),
            ("game_version", Application.version)
        );
        LogPathProbe();
        LogBuildChannelProbe();
        AndroidPatchHost.Install(HarmonyInstance, mode, version);
        _nextSummaryAt = Environment.TickCount64 + SummaryIntervalMs;
    }

    public override void OnUpdate()
    {
        if (Environment.TickCount64 < _nextSummaryAt)
            return;
        _nextSummaryAt = Environment.TickCount64 + SummaryIntervalMs;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.hooks.summary"),
            AndroidPatchHost.Summary()
        );
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.scene.loaded"),
            ("index", buildIndex),
            ("name", sceneName)
        );

    private static AndroidPatchMode ReadPatchMode()
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, SettingsFileName);
        try
        {
            return IniSettingsStore
                .Open(path)
                .Bind(
                    "Android",
                    "PatchMode",
                    AndroidPatchMode.Enabled,
                    "Enabled installs every hook; Observe installs them but changes nothing; "
                        + "Disabled installs none. Read at startup."
                )
                .Value;
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.settings.load_failed"),
                exception,
                ("path", path)
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
    private static void LogBuildChannelProbe()
    {
        try
        {
            var serverOption = AccessTools.Inner(typeof(Il2CppTheBazaar.Config), "ServerOption");
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.build_channel.probe"),
                ("game_version", Application.version),
                ("server_option", serverOption != null ? "found" : "missing")
            );
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.build_channel.probe_failed"),
                exception
            );
        }
    }
}
