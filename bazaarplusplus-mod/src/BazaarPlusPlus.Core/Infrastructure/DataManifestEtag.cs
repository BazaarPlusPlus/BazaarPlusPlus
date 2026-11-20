#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Infrastructure;

// Use the manifest ETag as the cache key because the game rewrites its data files on every download.
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
