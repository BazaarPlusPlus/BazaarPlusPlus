#nullable enable
namespace BazaarPlusPlus.Infrastructure.ReleaseManifest;

/// <summary>
/// Where the mod reads the published Product Release version from. Each platform has a dedicated
/// mod endpoint carrying its Platform Release Manifest; the lockstep manifest at
/// <c>latest.json</c> names the newest release Windows and macOS promoted together and serves any
/// platform without a manifest of its own. Android has only its Platform Release Manifest, and its
/// host passes <see cref="AndroidPlatformKey"/>.
/// </summary>
public static class ReleaseManifestEndpoints
{
    public const string WindowsPlatformKey = "windows-x86_64";
    public const string MacPlatformKey = "darwin-aarch64";
    public const string AndroidPlatformKey = "android-aarch64";

    private const string ReleaseBaseUrl = "https://cdn.bazaarplusplus.com/installer";
    private const string ReleaseManifestPath = "latest.json";

    public static Uri ForPlatformKey(string? platformKey) =>
        platformKey is WindowsPlatformKey or MacPlatformKey or AndroidPlatformKey
            ? new Uri($"{ReleaseBaseUrl}/latest/mod/{platformKey}.json")
            : new Uri($"{ReleaseBaseUrl}/{ReleaseManifestPath}");
}
