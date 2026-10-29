using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Ios.Service
{
    // The packaged game definition (game/game.json): the one game this App runs and how to prepare it.
    public sealed class GameDefinition
    {
        public JObject Game { get; }
        public string Platform { get; }
        public GameDefinition(string assets, string platform)
        {
            if (platform != "simulator" && platform != "device") throw new InvalidDataException("Unknown platform");
            Platform = platform;
            var game = JObject.Parse(File.ReadAllText(Path.Combine(assets, "game.json")));
            Game = game;
            {
                string id = (string?)game["id"] ?? "";
                if (!Regex.IsMatch(id, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z"))
                    throw new InvalidDataException("Invalid game ID");
                if ((uint?)game["steam"]?["appId"] == null || (uint)game["steam"]!["appId"]! == 0 ||
                    string.IsNullOrWhiteSpace((string?)game["displayName"]))
                    throw new InvalidDataException("Invalid game profile");
                foreach (string field in new[] { "dataPath", "installPath", "unityVersion", "testedGameVersion" })
                    if (string.IsNullOrWhiteSpace((string?)game[field])) throw new InvalidDataException("Missing game metadata");
                DownloadPaths.Resolve("/game-validation", (string)game["dataPath"]!);
                DownloadPaths.Resolve("/game-validation", (string)game["installPath"]!);
                if (!(game["steam"]?["macDepots"] is JArray depots) || depots.Count == 0 ||
                    depots.Any(d => d.Type != JTokenType.Integer || (long)d <= 0 || (long)d > uint.MaxValue))
                    throw new InvalidDataException("Invalid depot list");
            }
        }
        public bool CanLaunch => (bool?)Game["library"]?["platforms"]?[Platform]?["launch"] == true;
        public static string RuntimeRoot(string root, JObject game) =>
            DownloadPaths.Resolve(Path.Combine(root, "runtimes"), (string)game["id"]!);
        public static bool IsDownloaded(string root, JObject game)
        {
            string content = Downloader.ContentRoot(root, (uint)game["steam"]!["appId"]!);
            return ((JArray)game["steam"]!["macDepots"]!).All(id => {
                try {
                    var receipt = JObject.Parse(File.ReadAllText(Path.Combine(content, "verified-" + (uint)id + ".json")));
                    return (uint?)receipt["appId"] == (uint)game["steam"]!["appId"]! &&
                        (uint?)receipt["depotId"] == (uint)id && (long?)receipt["files"] > 0;
                } catch (IOException) { return false; }
                catch (Newtonsoft.Json.JsonException) { return false; }
                catch (FormatException) { return false; }
            });
        }
    }
}
