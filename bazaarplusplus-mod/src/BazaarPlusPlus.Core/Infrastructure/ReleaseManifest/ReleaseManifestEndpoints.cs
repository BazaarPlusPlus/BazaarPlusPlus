#nullable enable
namespace BazaarPlusPlus.Infrastructure.ReleaseManifest;

// latest.json is the desktop lockstep fallback. Android has its own Platform Release Manifest.
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
