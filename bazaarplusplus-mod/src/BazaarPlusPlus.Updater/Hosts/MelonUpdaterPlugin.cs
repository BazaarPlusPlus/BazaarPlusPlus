#nullable enable
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.Updater;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(
    typeof(MelonUpdaterPlugin),
    "BazaarPlusPlus.Updater",
    UpdaterRun.Version,
    "BazaarPlusPlus"
)]
[assembly: HarmonyDontPatchAll]

namespace BazaarPlusPlus.Updater;

// LemonLoader scans Mods after OnPreInitialization but loads UserLibs earlier.
// A replaced UserLib takes effect on the next launch.
public sealed class MelonUpdaterPlugin : MelonPlugin
{
    public override void OnPreInitialization()
    {
        var log = new UpdaterLog(
            message => LoggerInstance.Msg(message),
            message => LoggerInstance.Warning(message),
            message => LoggerInstance.Error(message)
        );
        // The MelonLoader base directory is files/MelonLoader; payload paths are relative to files/.
        var installRoot = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(MelonEnvironment.MelonBaseDirectory)
        );
        if (installRoot == null)
        {
            log.Warn(
                "updater.install_root.missing",
                ("melon_base", MelonEnvironment.MelonBaseDirectory)
            );
            return;
        }
        var runtimeDigest = UpdaterRun.RuntimeDigest(
            LaunchRecord.PathIn(MelonEnvironment.UserDataDirectory),
            Environment.ProcessId,
            log
        );
        UpdaterRun.Run(
            installRoot,
            ManagedPayloadHost.MelonLoader,
            UpdaterRun.AndroidPlatformKey,
            log,
            runtimeDigest
        );
    }
}
