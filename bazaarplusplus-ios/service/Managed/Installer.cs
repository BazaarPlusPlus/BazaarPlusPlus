using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Ios.Service
{
    internal static class Installer
    {
        public static void Prepare(JObject profile, string root, string assets, System.Threading.CancellationToken cancellation)
        {
            SteamService.Set("preparing", "正在准备游戏");
            uint app = (uint)profile["steam"]!["appId"]!;
            string bundle = Path.GetFileName((string)profile["installPath"]!);
            string content = Path.Combine(Downloader.ContentRoot(root, app), bundle);
            var plist = XDocument.Load(Path.Combine(content, "Contents/Info.plist"));
            string version = plist.Descendants("key").First(x => x.Value == "CFBundleShortVersionString").ElementsAfterSelf().First().Value;
            if (version != (string)profile["testedGameVersion"]!) throw new GameVersionUnsupportedException(version, (string)profile["testedGameVersion"]!);
            string source = Path.Combine(content, (string)profile["dataPath"]!);
            byte[] header = File.ReadAllBytes(Path.Combine(source, "globalgamemanagers")).Take(512).ToArray();
            if (!System.Text.Encoding.ASCII.GetString(header).Contains((string)profile["unityVersion"]! + "\0")) throw new InvalidDataException("Unvalidated Unity version");
            using (var sha = SHA256.Create())
            {
                byte[] installed = sha.ComputeHash(File.ReadAllBytes(Path.Combine(source, "Managed/mscorlib.dll")));
                byte[] built = sha.ComputeHash(File.ReadAllBytes(Path.Combine(assets, "Data/Managed/mscorlib.dll")));
                if (!installed.SequenceEqual(built)) throw new InvalidDataException("Runtime corlib differs");
            }
            cancellation.ThrowIfCancellationRequested();
            string target = Path.Combine(GameDefinition.RuntimeRoot(root, profile), "Data"); Directory.CreateDirectory(Path.Combine(target, "Managed"));
            // The relocated Data directory also needs the iOS player's built-in resources.
            string engineResources = Path.Combine(assets, "Data/Resources");
            foreach (string path in Directory.EnumerateFiles(engineResources, "*", SearchOption.AllDirectories))
            {
                cancellation.ThrowIfCancellationRequested();
                string destination = Path.Combine(target, "Resources", path.Substring(engineResources.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(path, destination, true);
            }
            File.Copy(Path.Combine(assets, "Data/boot.config"), Path.Combine(target, "boot.config"), true);
            foreach (var path in Directory.EnumerateFiles(source))
            {
                cancellation.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (new[] { "globalgamemanagers", "level", "sharedassets", "resources" }.Any(p => name.StartsWith(p, StringComparison.Ordinal)) ||
                    name == "ScriptingAssemblies.json" || name == "RuntimeInitializeOnLoads.json") File.Copy(path, Path.Combine(target, name), true);
            }
            foreach (var path in Directory.EnumerateFiles(Path.Combine(source, "Managed"), "*.dll")) File.Copy(path, Path.Combine(target, "Managed", Path.GetFileName(path)), true);
            foreach (var path in Directory.EnumerateFiles(Path.Combine(assets, "Data/Managed"), "*.dll"))
                if (Path.GetFileName(path).StartsWith("UnityEngine", StringComparison.Ordinal) || !File.Exists(Path.Combine(target, "Managed", Path.GetFileName(path))))
                    File.Copy(path, Path.Combine(target, "Managed", Path.GetFileName(path)), true);
            File.Copy(Path.Combine(assets, "BazaarPlusPlus.Ios.Helper.dll"), Path.Combine(target, "Managed/BazaarPlusPlus.Ios.Helper.dll"), true);
            // The mod and its libraries sit next to the game's assemblies, as the Mono host expects.
            foreach (var path in Directory.EnumerateFiles(Path.Combine(assets, "Mods")))
                File.Copy(path, Path.Combine(target, "Managed", Path.GetFileName(path)), true);
            ManagedAdapters.Apply(profile, Path.Combine(source, "Managed"), Path.Combine(target, "Managed"), assets);
            // Entry points without an assembly name are the helper's; each named assembly is registered once.
            var added = JArray.Parse(File.ReadAllText(Path.Combine(assets, "entrypoints.json"))).Cast<JObject>().ToList();
            foreach (var entry in added) { entry["assemblyName"] ??= "BazaarPlusPlus.Ios.Helper"; entry["isUnityClass"] = false; }
            var owners = added.Select(e => (string)e["assemblyName"]!).Distinct().ToList();
            var assemblies = JObject.Parse(File.ReadAllText(Path.Combine(target, "ScriptingAssemblies.json")));
            foreach (string owner in owners)
                if (!((JArray)assemblies["names"]!).Any(x => (string)x! == owner + ".dll"))
                { ((JArray)assemblies["names"]!).Add(owner + ".dll"); ((JArray)assemblies["types"]!).Add(16); }
            File.WriteAllText(Path.Combine(target, "ScriptingAssemblies.json"), assemblies.ToString());
            var loads = JObject.Parse(File.ReadAllText(Path.Combine(target, "RuntimeInitializeOnLoads.json")));
            var entries = new JArray(((JArray)loads["root"]!).Where(x => !owners.Contains((string?)x["assemblyName"] ?? "")));
            foreach (var entry in added) entries.Add(entry);
            loads["root"] = entries; File.WriteAllText(Path.Combine(target, "RuntimeInitializeOnLoads.json"), loads.ToString());
            string raw = Path.Combine(target, "Raw");
            // App updates may relocate the entire data container. Never retain an absolute link.
            try { if ((File.GetAttributes(raw) & FileAttributes.ReparsePoint) != 0) File.Delete(raw); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            string relativeAssets = Path.Combine("../../../content", app.ToString(), bundle, (string)profile["dataPath"]!, "StreamingAssets");
            if (!Directory.Exists(raw) && Native.bpp_steam_symlink(relativeAssets, raw) != 0)
                throw new IOException("Failed to attach streaming assets");
            cancellation.ThrowIfCancellationRequested();
            Console.WriteLine("STEAM_APP_GAME_PREPARED");
        }
    }

    // Steam serves the newest game build; the App runs only the build its adapters and mod were tested on.
    public sealed class GameVersionUnsupportedException : IOException
    {
        public string Found { get; }
        public string Tested { get; }
        public GameVersionUnsupportedException(string found, string tested) : base("Unvalidated game version")
        { Found = found; Tested = tested; }
    }
}
