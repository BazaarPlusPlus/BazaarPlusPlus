#nullable enable
using System.Reflection;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.Supporters;

internal static class SupporterCatalogFactory
{
    private static readonly HttpClient HttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        platformKey: HostPlatformKey.Current,
        userAgentSuffix: "BPPSupporterCatalog",
        timeout: TimeSpan.FromSeconds(10)
    );

    internal static IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>> Create(
        string dataRootPath,
        IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>> observer
    )
    {
        var cache = new FileCatalogCache(SupporterCatalogSettings.BuildCacheFilePath(dataRootPath));
        return new RemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>(
            new SupporterCatalogParser(),
            new AssemblyResourceCatalogSource(
                Assembly.GetExecutingAssembly(),
                SupporterCatalogSettings.EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(HttpClient, SupporterCatalogSettings.RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            observer,
            SupporterCatalogSettings.CacheDuration
        );
    }
}
