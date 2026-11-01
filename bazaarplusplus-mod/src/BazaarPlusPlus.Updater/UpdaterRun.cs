#nullable enable
using System.Runtime.InteropServices;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Updater;

/// <summary>
/// One Updater run for a host: the Data Root sits beside the host's install root on both hosts
/// (<c>&lt;GameRoot&gt;/BazaarPlusPlusV6</c>, <c>files/BazaarPlusPlusV6</c>). Nothing escapes;
/// the game must start whatever happens here.
/// </summary>
internal static class UpdaterRun
{
    /// <summary>PathConstants.DataRootDirectoryName, which the Updater may not reference.</summary>
    public const string DataRootDirectoryName = "BazaarPlusPlusV6";

    /// <summary>
    /// The Updater's own version; it ships only with the installer or the APK. MelonInfo needs a
    /// constant, so this repeats the csproj Version; a test pins the built assembly to it.
    /// </summary>
    public const string Version = "1.0.0";

    // ReleaseManifestEndpoints' platform keys, which the Updater may not reference; a test pins
    // them together.
    public const string MacPlatformKey = "darwin-aarch64";
    public const string WindowsPlatformKey = "windows-x86_64";
    public const string AndroidPlatformKey = "android-aarch64";

    public static string? DesktopPlatformKey() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacPlatformKey
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsPlatformKey
        : null;

    // runtimeDigest: the running runtime's non-managed payload digest from the Android App's
    // Launch Record (RuntimeDigest); null compares with the installed manifest's.
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
            var dataRoot = Path.Combine(installRoot, DataRootDirectoryName);
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
    /// <summary>
    /// The digest of the runtime the Android App started this process with: its Launch Record's
    /// <c>app.nonManagedPayloadDigest</c>, when the record parses and this process wrote it (root
    /// ADR 0012). No record, an unreadable one, another process's, or a development App's (no
    /// digest) yields null, which keeps the installed manifest's rule. The record's release is
    /// not checked: it is the version this launch may swap in.
    /// </summary>
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
