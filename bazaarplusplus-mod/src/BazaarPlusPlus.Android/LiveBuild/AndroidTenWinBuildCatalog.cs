using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// The ten-win build catalog on Android: the Remote Embedded Catalog the desktop's
/// <c>TenWinBuildCatalogFactory</c> builds, over the same parser, observer, URL, cache file name,
/// and seed resource (<see cref="TenWinBuildCatalogSettings"/>), with the seed embedded in this
/// assembly and the cache in the Android Data Root. The composition root owns the one instance
/// and disposes it on stop. A developer may point the remote fetch at a local server
/// (<c>[AndroidDev] BuildCorpusPort</c>); the default is the CDN.
/// </summary>
internal static class AndroidTenWinBuildCatalog
{
    public static IRemoteEmbeddedCatalog<TenWinBuildCorpus> Create(
        string dataRoot,
        int devCorpusPort = 0
    )
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
                TenWinBuildCatalogSettings.EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(client, RemoteUrl(devCorpusPort)),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            new TenWinBuildCatalogObserver(cache.FilePath),
            TenWinBuildCatalogSettings.CacheDuration
        );
    }

    /// <summary>The CDN, or a local developer server when <paramref name="devCorpusPort"/> is set.</summary>
    internal static string RemoteUrl(int devCorpusPort)
    {
        if (devCorpusPort <= 0 || devCorpusPort > 65535)
            return TenWinBuildCatalogSettings.RemoteUrl;
        var url =
            $"http://127.0.0.1:{devCorpusPort}{new Uri(TenWinBuildCatalogSettings.RemoteUrl).AbsolutePath}";
        BppLog.WarnEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.dev_corpus_override"),
            ("url", url)
        );
        return url;
    }
}
