using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Android.Supporters;

/// <summary>
/// The supporter list on Android: the Remote Embedded Catalog the desktop's
/// <c>SupporterCatalogFactory</c> builds, over the same parser, URL, cache file name, and seed
/// resource (<see cref="SupporterCatalogSettings"/>), with the desktop's seed file embedded in
/// this assembly and the cache in the Android Data Root.
/// </summary>
internal static class AndroidSupporterCatalog
{
    public static IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>> Create(
        string dataRoot,
        IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>> observer
    )
    {
        var cache = new FileCatalogCache(SupporterCatalogSettings.BuildCacheFilePath(dataRoot));
        var client = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            platformKey: ReleaseManifestEndpoints.AndroidPlatformKey,
            userAgentSuffix: "BPPSupporterCatalog",
            timeout: TimeSpan.FromSeconds(10)
        );
        return new RemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>(
            new SupporterCatalogParser(),
            new AssemblyResourceCatalogSource(
                typeof(AndroidSupporterCatalog).Assembly,
                SupporterCatalogSettings.EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(client, SupporterCatalogSettings.RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            observer,
            SupporterCatalogSettings.CacheDuration
        );
    }
}
