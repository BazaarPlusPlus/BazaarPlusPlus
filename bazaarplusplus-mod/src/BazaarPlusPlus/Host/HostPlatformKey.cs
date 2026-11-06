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
    // RuntimeInformation instead of UnityEngine.Application.platform: the HTTP clients that read
    // this are created in static initializers that test processes run without a Unity runtime.
    public static string? Current { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ReleaseManifestEndpoints.MacPlatformKey
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ReleaseManifestEndpoints.WindowsPlatformKey
        : null;
}
