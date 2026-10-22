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

    public static SwapOutcome? Run(
        string installRoot,
        ManagedPayloadHost host,
        string? platformKey,
        UpdaterLog log
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
                log
            ).Run();
        }
        catch (Exception exception)
        {
            log.Error("updater.failed", exception);
            return null;
        }
    }
}
