using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Android.Supporters;

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
