#nullable enable
using System.Runtime.InteropServices;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;

namespace BazaarPlusPlus.Host;

/// <summary>
/// The release platform key of the desktop player this process runs in, or null off the two
/// published desktop platforms. It selects the Platform Release Manifest and is the platform
/// token every outbound User-Agent carries.
/// </summary>
internal static class DesktopPlatformKey
{
    // RuntimeInformation instead of UnityEngine.Application.platform: the HTTP clients that read
    // this are created in static initializers that test processes run without a Unity runtime.
    public static string? Current { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ReleaseManifestEndpoints.MacPlatformKey
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ReleaseManifestEndpoints.WindowsPlatformKey
        : null;
}
