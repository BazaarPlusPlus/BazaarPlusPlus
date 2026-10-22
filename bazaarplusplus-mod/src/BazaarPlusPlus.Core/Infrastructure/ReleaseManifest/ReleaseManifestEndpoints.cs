#nullable enable
namespace BazaarPlusPlus.Infrastructure.ReleaseManifest;

/// <summary>
/// Where the mod reads the published Product Release version from. Each platform has its own
/// Platform Release Manifest and may be promoted ahead of the other; the lockstep manifest at
/// <c>latest.json</c> names the newest release every platform promoted together and serves any
/// platform without a manifest of its own.
/// </summary>
public static class ReleaseManifestEndpoints
{
    public const string WindowsPlatformKey = "windows-x86_64";
    public const string MacPlatformKey = "darwin-aarch64";

    private const string ReleaseBaseUrl = "https://cdn.bazaarplusplus.com/installer";
    private const string ReleaseManifestPath = "latest.json";

    public static Uri ForPlatformKey(string? platformKey) =>
        platformKey is WindowsPlatformKey or MacPlatformKey
            ? new Uri($"{ReleaseBaseUrl}/latest/{platformKey}.json")
            : new Uri($"{ReleaseBaseUrl}/{ReleaseManifestPath}");
}
