#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Core.Runtime;

// The game writes the cache manifest before rebuilding its data manager and updates it before Events.RunStarted.
// Strip HTTP quotes to match the ETag form used by release/card-art.ts.
public static class GameDataManifest
{
    public const string FileName = "manifest.json";

    public static string? ReadGameDataEtag(string? cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            return null;
        try
        {
            var path = Path.Combine(cacheDirectory, FileName);
            if (!File.Exists(path))
                return null;
            var etag = JObject
                .Parse(File.ReadAllText(path))
                .SelectToken("Entries.GameData.ETag")
                ?.Value<string>()
                ?.Trim()
                .Trim('"');
            return string.IsNullOrEmpty(etag) ? null : etag;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
