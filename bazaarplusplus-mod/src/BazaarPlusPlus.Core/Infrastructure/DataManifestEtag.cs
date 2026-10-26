#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Infrastructure;

/// <summary>
/// An entry's ETag in one of the game's data manifests: <c>prod/cache/manifest.json</c> names the
/// downloaded game data (<c>GameData</c>), <c>prod/cache/translations/manifest.json</c> each
/// downloaded locale. BPP's caches key on it, since the game rewrites the files themselves on
/// every download.
/// </summary>
public static class DataManifestEtag
{
    public static bool TryRead(string manifestPath, string entry, out string etag)
    {
        etag = string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return false;

        try
        {
            var document = JObject.Parse(File.ReadAllText(manifestPath));
            var entryObject = (document["Entries"] as JObject)?[entry] as JObject;
            etag = (entryObject?["ETag"] as JValue)?.Value as string ?? string.Empty;
            return !string.IsNullOrWhiteSpace(etag);
        }
        catch (Exception ex)
            when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is Newtonsoft.Json.JsonException
            )
        {
            etag = string.Empty;
            return false;
        }
    }
}
