#nullable enable
using System.Reflection;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal static class TenWinBuildCatalogFactory
{
    private static readonly HttpClient HttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        platformKey: HostPlatformKey.Current,
        userAgentSuffix: "TenWinBuildRepository",
        timeout: TimeSpan.FromSeconds(10)
    );

    internal static IRemoteEmbeddedCatalog<TenWinBuildCorpus> Create(string dataRootPath)
    {
        var cache = new FileCatalogCache(
            TenWinBuildCatalogSettings.BuildCacheFilePath(dataRootPath)
        );
        return new RemoteEmbeddedCatalog<TenWinBuildCorpus>(
            new TenWinBuildCatalogParser(),
            new AssemblyResourceCatalogSource(
                Assembly.GetExecutingAssembly(),
                TenWinBuildCatalogSettings.EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(HttpClient, TenWinBuildCatalogSettings.RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            new TenWinBuildCatalogObserver(cache.FilePath),
            TenWinBuildCatalogSettings.CacheDuration
        );
    }
}
