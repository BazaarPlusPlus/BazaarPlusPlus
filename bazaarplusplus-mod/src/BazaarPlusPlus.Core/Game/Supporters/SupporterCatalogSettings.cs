#nullable enable
namespace BazaarPlusPlus.Game.Supporters;

// Both hosts embed the same supporter-list seed. A compiled-artifact test pins its resource name.
public static class SupporterCatalogSettings
{
    public const string EmbeddedResourceName = "BazaarPlusPlus.Game.Supporters.supporter-list.json";
    public const string RemoteUrl =
        "https://cdn.bazaarplusplus.com/mod-data/supporters/supporter-list.json";
    public const string CacheFileName = "supporter-list.json";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public static string BuildCacheFilePath(string dataRootPath) =>
        Path.Combine(dataRootPath, CacheFileName);
}
