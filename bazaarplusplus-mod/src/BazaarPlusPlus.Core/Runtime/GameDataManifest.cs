#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Core.Runtime;

/// <summary>
/// Reads the ETag of the <c>GameData.db</c> the game has loaded from the manifest the game keeps
/// beside it: <c>manifest.json</c> in its data cache directory
/// (<c>TheBazaar.DataManagement.DataManifestActions.GetCachePath()</c>), shaped
/// <c>{"Entries":{"GameData":{"ETag":"\"…\""}}}</c>. The game writes the entry after a download and
/// before it rebuilds its data manager, and refreshes it at every run start before
/// <c>Events.RunStarted</c>, so the entry names the loaded data from then on.
/// The value has its HTTP quotes removed, the form <c>release/card-art.ts</c> (<c>gameDataEtag</c>)
/// reads from the public URL. A missing file or entry (the game fell back to its bundled data), a
/// malformed file, or a read error gives null: the read must never block the run it describes.
/// </summary>
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
