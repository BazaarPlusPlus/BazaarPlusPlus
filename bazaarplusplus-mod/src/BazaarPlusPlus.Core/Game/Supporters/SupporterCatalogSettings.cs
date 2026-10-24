#nullable enable
namespace BazaarPlusPlus.Game.Supporters;

/// <summary>
/// Where every host fetches and caches the supporter list. Each host's composition root supplies
/// the transport and the data root, and embeds the one seed file
/// (<c>src/BazaarPlusPlus/Game/Supporters/supporter-list.json</c>) under
/// <see cref="EmbeddedResourceName"/>, which its MSBuild item restates and a compiled-artifact
/// test pins.
/// </summary>
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
