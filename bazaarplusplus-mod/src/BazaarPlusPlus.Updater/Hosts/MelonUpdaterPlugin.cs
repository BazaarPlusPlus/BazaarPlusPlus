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

/// <summary>
/// The Android Host form: a MelonPlugin in <c>MelonLoader/Plugins/</c>. LemonLoader loads
/// plugins and raises <see cref="OnPreInitialization"/> before it scans <c>Mods/</c>, so the
/// swapped Mods are the ones this launch loads. <c>UserLibs/</c> are already loaded by then; a
/// swapped UserLib takes effect from the next launch.
/// </summary>
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
        UpdaterRun.Run(
            installRoot,
            ManagedPayloadHost.MelonLoader,
            UpdaterRun.AndroidPlatformKey,
            log
        );
    }
}
