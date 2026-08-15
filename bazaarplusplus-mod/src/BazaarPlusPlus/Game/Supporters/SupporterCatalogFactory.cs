#nullable enable
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.Supporters;

internal static class SupporterCatalogFactory
{
    internal const string EmbeddedResourceName =
        "BazaarPlusPlus.Game.Supporters.supporter-list.json";
    private const string RemoteUrl = "https://bpp-static.bazaarplusplus.com/supporter-list.json";
    private const string CacheFileName = "supporter-list.json";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly HttpClient HttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "BPPSupporterCatalog",
        timeout: TimeSpan.FromSeconds(10)
    );

    internal static IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>> Create(
        string dataRootPath,
        IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>> observer
    )
    {
        var cache = new FileCatalogCache(BuildCacheFilePath(dataRootPath));
        return new RemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>(
            new SupporterCatalogParser(),
            new AssemblyResourceCatalogSource(
                Assembly.GetExecutingAssembly(),
                EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(HttpClient, RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            observer,
            CacheDuration
        );
    }

    internal static string BuildCacheFilePath(string dataRootPath) =>
        Path.Combine(dataRootPath, CacheFileName);
}
