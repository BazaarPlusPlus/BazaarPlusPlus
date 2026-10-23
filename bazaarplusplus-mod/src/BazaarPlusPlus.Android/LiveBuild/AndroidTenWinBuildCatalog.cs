using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// The ten-win build catalog on Android: the Remote Embedded Catalog the desktop's
/// <c>TenWinBuildCatalogFactory</c> builds, over the same parser, observer, URL, and cache file name,
/// with the seed embedded in this assembly and the cache in the Android Data Root. The composition
/// root owns the one instance and disposes it on stop.
/// </summary>
internal static class AndroidTenWinBuildCatalog
{
    /// <summary>The seed's resource name, the desktop's, so one seed file serves both hosts.</summary>
    public const string EmbeddedResourceName =
        "BazaarPlusPlus.Data.BuildRecommendations.builds.json";

    public static IRemoteEmbeddedCatalog<TenWinBuildCorpus> Create(string dataRoot)
    {
        var cache = new FileCatalogCache(TenWinBuildCatalogSettings.BuildCacheFilePath(dataRoot));
        var client = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            platformKey: ReleaseManifestEndpoints.AndroidPlatformKey,
            userAgentSuffix: "TenWinBuildRepository",
            timeout: TimeSpan.FromSeconds(10)
        );
        return new RemoteEmbeddedCatalog<TenWinBuildCorpus>(
            new TenWinBuildCatalogParser(),
            new AssemblyResourceCatalogSource(
                typeof(AndroidTenWinBuildCatalog).Assembly,
                EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(client, TenWinBuildCatalogSettings.RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            new TenWinBuildCatalogObserver(cache.FilePath),
            TenWinBuildCatalogSettings.CacheDuration
        );
    }
}
