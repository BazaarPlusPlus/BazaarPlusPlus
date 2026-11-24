#nullable enable
using System.Runtime.InteropServices;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Updater;

// Updater failures must leave the loader free to start the game.
internal static class UpdaterRun
{
    /// <summary>PathConstants.DataRootDirectoryName, which the Updater may not reference.</summary>
    public const string DataRootDirectoryName = "BazaarPlusPlusV6";

    // MelonInfo requires a constant. A test pins it to the assembly's fixed project version.
    public const string Version = "1.0.0";

    // ReleaseManifestEndpoints' platform keys, which the Updater may not reference; a test pins
    // them together.
    public const string MacPlatformKey = DesktopDataRoot.MacPlatformKey;
    public const string WindowsPlatformKey = DesktopDataRoot.WindowsPlatformKey;
    public const string AndroidPlatformKey = "android-aarch64";

    public static string? DesktopPlatformKey() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacPlatformKey
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsPlatformKey
        : null;

    public static SwapOutcome? Run(
        string installRoot,
        ManagedPayloadHost host,
        string? platformKey,
        UpdaterLog log,
        string? runtimeDigest = null
    )
    {
        try
        {
            var dataRoot = DesktopDataRoot.Resolve(
                installRoot,
                DataRootDirectoryName,
                platformKey,
                message => log.Warn("updater.data_root.migration", ("detail", message))
            );
            log.Info(
                "updater.started",
                ("version", Version),
                ("platform", platformKey),
                ("install_root", installRoot)
            );
            if (platformKey == null)
                return null;
            return new ManagedPayloadSwap(
                installRoot,
                dataRoot,
                host,
                platformKey,
                ReleaseSigningKey.PublicKey,
                log,
                runtimeDigest
            ).Run();
        }
        catch (Exception exception)
        {
            log.Error("updater.failed", exception);
            return null;
        }
    }

#if NET
    // A record from this process supplies the runtime digest before any Mods load.
    // Its release is the one this launch may swap in.
    public static string? RuntimeDigest(string recordPath, int processId, UpdaterLog log)
    {
        try
        {
            var reading = LaunchRecordReading.Read(recordPath, processId, runningRelease: null);
            var digest = reading.Record?.App.NonManagedPayloadDigest;
            log.Info(
                "updater.launch_record.read",
                ("mode", reading.Mode),
                ("reason", reading.Reason),
                ("runtime_digest", digest == null ? "none" : "present")
            );
            return digest;
        }
        catch (Exception exception)
        {
            log.Error("updater.launch_record.failed", exception);
            return null;
        }
    }
#endif
}
