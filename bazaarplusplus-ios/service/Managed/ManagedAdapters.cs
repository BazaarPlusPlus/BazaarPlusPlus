using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Ios.Service
{
    internal static class ManagedAdapters
    {
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        public static void Apply(JObject profile, string source, string target, string assets)
        {
            if (!(profile["managedAdapters"] is JArray adapters)) return;
            string folder = Path.Combine(assets, "GameAdapters");
            var receipt = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            foreach (JObject adapter in adapters)
            {
                string name = (string)adapter["assembly"]!;
                string original = DownloadPaths.Resolve(source, name);
                string patched = DownloadPaths.Resolve(folder, name);
                if ((string?)receipt["gameId"] != (string?)profile["id"] ||
                    (string?)receipt["adapter"] != (string?)adapter["adapter"] ||
                    (string?)receipt["assembly"] != name ||
                    (string?)receipt["sourceSha256"] != (string?)adapter["sourceSha256"] ||
                    Hash(original) != (string?)adapter["sourceSha256"] ||
                    Hash(patched) != (string?)receipt["patchedSha256"])
                    throw new InvalidDataException("Unvalidated managed game adapter: " + name);
                File.Copy(patched, DownloadPaths.Resolve(target, name), true);
            }
        }
    }
}
