using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Android.LiveBuild;

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
