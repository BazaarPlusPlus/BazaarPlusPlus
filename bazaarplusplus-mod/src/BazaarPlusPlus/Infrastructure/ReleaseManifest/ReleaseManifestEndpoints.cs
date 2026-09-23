#nullable enable
namespace BazaarPlusPlus.Infrastructure.ReleaseManifest;

/// <summary>
/// Where the mod reads the published Product Release version from. Each platform has its own
/// Platform Release Manifest and may be promoted ahead of the other; the lockstep manifest at
/// <c>latest.json</c> names the newest release every platform promoted together and serves any
/// platform without a manifest of its own.
/// </summary>
internal static class ReleaseManifestEndpoints
{
    internal const string WindowsPlatformKey = "windows-x86_64";
    internal const string MacPlatformKey = "darwin-aarch64";

    private const string ReleaseOrigin = "https://bppinstaller.bazaarplusplus.com";
    private const string ReleaseManifestPath = "latest.json";

    internal static Uri ForPlatformKey(string? platformKey) =>
        platformKey is WindowsPlatformKey or MacPlatformKey
            ? new Uri($"{ReleaseOrigin}/latest/{platformKey}.json")
            : new Uri($"{ReleaseOrigin}/{ReleaseManifestPath}");
}
