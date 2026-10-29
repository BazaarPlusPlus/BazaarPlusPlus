#nullable enable
using System.Runtime.InteropServices;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;

namespace BazaarPlusPlus.Host;

/// <summary>
/// The platform key of the player this process runs in. On the desktop it selects the Platform
/// Release Manifest; on every host it is the platform token each outbound User-Agent carries.
/// </summary>
internal static class HostPlatformKey
{
    /// <summary>The iOS port: no Platform Release Manifest, the mod ships inside the App.</summary>
    public const string Ios = "ios-aarch64";

    // RuntimeInformation instead of UnityEngine.Application.platform: the HTTP clients that read
    // this are created in static initializers that test processes run without a Unity runtime.
    public static string? Current { get; private set; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ReleaseManifestEndpoints.MacPlatformKey
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ReleaseManifestEndpoints.WindowsPlatformKey
        : null;

    /// <summary>
    /// Called by the iOS host before anything reads <see cref="Current"/>: the iOS port runs the
    /// game's macOS class libraries, so RuntimeInformation reports macOS there.
    /// </summary>
    public static void UseIos() => Current = Ios;
}
