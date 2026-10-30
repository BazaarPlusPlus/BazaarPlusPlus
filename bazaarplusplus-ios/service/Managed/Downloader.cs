using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SteamKit2;
using SteamKit2.CDN;

namespace BazaarPlusPlus.Ios.Service
{
    public static class DownloadPaths
    {
        public static string Resolve(string root, string name)
        {
            name = name.Replace('\\', '/');
            if (name.Length == 0 || Path.IsPathRooted(name) || name.IndexOf('\0') >= 0 ||
                name.Split('/').Any(p => p == ".." || p == "." || p.Length == 0)) throw new InvalidDataException("Unsafe manifest path");
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Path escapes depot");
            for (string? parent = path; parent != null && parent.StartsWith(root, StringComparison.Ordinal); parent = Path.GetDirectoryName(parent))
            {
                try
                {
                    // File.Exists is false for dangling links; inspect the entry itself.
                    if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Symlink in depot path");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return path;
        }
    }
    internal sealed class NoHttpsContentServerException : IOException { }
    public static class PartialFile
    {
        // Chunks a cancelled or killed download already wrote pass their checksum and are kept.
        public static List<DepotManifest.ChunkData> Missing(Stream partial, IEnumerable<DepotManifest.ChunkData> chunks)
        {
            var missing = new List<DepotManifest.ChunkData>();
            var data = Array.Empty<byte>();
            foreach (var chunk in chunks.OrderBy(c => c.Offset))
            {
                // Chunks share one size except the tail; reuse the buffer rather than feed the deferred collector.
                if (data.Length != chunk.UncompressedLength) data = new byte[chunk.UncompressedLength];
                partial.Position = checked((long)chunk.Offset);
                int read = 0, count;
                while (read < data.Length && (count = partial.Read(data, read, data.Length - read)) > 0) read += count;
                if (read != data.Length || !CryptoHelper.AdlerHash(data).SequenceEqual(chunk.Checksum!)) missing.Add(chunk);
            }
            return missing;
        }
    }
    sealed class DownloadProgress
    {
        readonly long total;
        readonly object gate = new object();
        long done, received, lastReceived, lastTicks = DateTime.UtcNow.Ticks;
        double speed;
        public DownloadProgress(long total) { this.total = total; }
        public void Verified(long bytes) => Add(bytes, 0);
        public void Downloaded(long bytes) => Add(bytes, bytes);
        void Add(long bytes, long network)
        {
            lock (gate)
            {
                done += bytes; received += network;
                long now = DateTime.UtcNow.Ticks;
                double seconds = (double)(now - lastTicks) / TimeSpan.TicksPerSecond;
                if (seconds < 1) return;
                double sample = (received - lastReceived) / seconds;
                speed = speed == 0 ? sample : speed * 0.7 + sample * 0.3;
                lastReceived = received; lastTicks = now;
                SteamService.Set("downloading", received > 0 ? "正在下载" : "正在校验本地文件", total > 0 ? (double)done / total : 0, done, total, (long)speed);
            }
        }
    }
    internal static class Downloader
    {
        static readonly object collectionGate = new object();
        // Unity's conservative collector can defer collection past iOS's process
        // memory budget. Downloading has no frame loop to request collections.
        static void CollectDownloadBuffers()
        {
            const long budget = 64L * 1024 * 1024;
            if (GC.GetTotalMemory(false) < budget) return;
            lock (collectionGate)
            {
                if (GC.GetTotalMemory(false) < budget) return;
                GC.Collect();
            }
        }
        public static string ContentRoot(string root, uint app) => Path.Combine(root, "content", app.ToString());
        public static async Task Download(SteamClient client, JObject profile, string root, CancellationToken cancellation)
        {
            uint app = (uint)profile["steam"]!["appId"]!;
            string destination = ContentRoot(root, app); Directory.CreateDirectory(destination);
            var apps = client.GetHandler<SteamApps>()!;
            SteamService.Set("downloading", "正在获取游戏清单");
            var access = await apps.PICSGetAccessTokens(new[] { app }, Array.Empty<uint>());
            access.AppTokens.TryGetValue(app, out ulong token);
            var info = await apps.PICSGetProductInfo(new[] { new SteamApps.PICSRequest(app, token) }, Array.Empty<SteamApps.PICSRequest>());
            var product = info.Results.SelectMany(r => r.Apps).First(r => r.Key == app).Value.KeyValues;
            var content = client.GetHandler<SteamContent>()!;
            var servers = await ContentServers.Load(client, app, client.CellID ?? 0);
            if (servers.Length == 0 && client.CellID.GetValueOrDefault() != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                servers = await ContentServers.Load(client, app, 0);
            }
            if (servers.Length == 0) servers = await ContentServers.LoadWeb(client, app, cancellation);
            if (servers.Length == 0) throw new NoHttpsContentServerException();
            // Mono's ServicePointManager allows two connections per host unless told otherwise.
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 32);
            using (var cdn = new Client(client))
            {
                foreach (var id in (JArray)profile["steam"]!["macDepots"]!)
                {
                    cancellation.ThrowIfCancellationRequested(); uint depot = (uint)id;
                    ulong manifestId = product["depots"][depot.ToString()]["manifests"]["public"]["gid"].AsUnsignedLong();
                    if (manifestId == 0) throw new InvalidDataException("No public manifest for selected depot");
                    var key = await apps.GetDepotDecryptionKey(depot, app);
                    if (key.Result != EResult.OK) throw new InvalidOperationException("Depot not owned: " + key.Result);
                    ulong code = await content.GetManifestRequestCode(depot, app, manifestId, "public");
                    if (code == 0) throw new InvalidOperationException("Manifest access denied");
                    DepotManifest? manifest = null;
                    for (int attempt = 0; attempt < servers.Length; ++attempt)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try { manifest = await cdn.DownloadManifestAsync(depot, manifestId, code, servers[attempt], key.DepotKey); break; }
                        catch when (attempt + 1 < servers.Length && !cancellation.IsCancellationRequested)
                        { Console.WriteLine("STEAM_CDN_MANIFEST_RETRY"); }
                    }
                    if (manifest == null) throw new InvalidDataException("Missing depot manifest");
                    if (manifest.FilenamesEncrypted || manifest.Files == null || manifest.DepotID != depot) throw new InvalidDataException("Invalid depot manifest");
                    long total = checked((long)manifest.TotalUncompressedSize);
                    var progress = new DownloadProgress(total);
                    int resumed = 0;
                    var pool = new ContentServerPool(servers);
                    SteamService.Set("downloading", "正在校验本地文件");
                    using (var files = new SemaphoreSlim(4))
                    using (var transfers = new SemaphoreSlim(16))
                    {
                        var tasks = manifest.Files.Select(async file =>
                        {
                            await files.WaitAsync(cancellation);
                            try
                            {
                                string path = DownloadPaths.Resolve(destination, file.FileName);
                                if ((file.Flags & EDepotFileFlag.Directory) != 0) { Directory.CreateDirectory(path); return; }
                                if (!string.IsNullOrEmpty(file.LinkTarget)) throw new InvalidDataException("Depot symlinks are not supported");
                                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                                if (File.Exists(path) && new FileInfo(path).Length == (long)file.TotalSize && HashMatches(path, file.FileHash))
                                { progress.Verified((long)file.TotalSize); return; }
                                foreach (var chunk in file.Chunks)
                                    if (chunk.UncompressedLength > 16 * 1024 * 1024 || chunk.CompressedLength > 32 * 1024 * 1024 ||
                                        chunk.Offset + chunk.UncompressedLength > file.TotalSize) throw new InvalidDataException("Invalid chunk bounds");
                                string partial = DownloadPaths.Resolve(destination, file.FileName + ".download");
                                using (var output = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                                {
                                    var missing = file.Chunks.ToList();
                                    if (output.Length == (long)file.TotalSize)
                                    {
                                        missing = PartialFile.Missing(output, file.Chunks);
                                        long kept = (long)file.TotalSize - missing.Sum(c => (long)c.UncompressedLength);
                                        if (kept > 0) { progress.Verified(kept); Interlocked.Increment(ref resumed); }
                                    }
                                    else output.SetLength(checked((long)file.TotalSize));
                                    await Task.WhenAll(missing.Select(async chunk =>
                                    {
                                        await transfers.WaitAsync(cancellation);
                                        try
                                        {
                                            DepotChunk? downloaded = null;
                                            for (int attempt = 0; attempt < 4; ++attempt)
                                            {
                                                int node = pool.Rent(); var clock = Stopwatch.StartNew();
                                                try
                                                {
                                                    downloaded = await cdn.DownloadDepotChunkAsync(depot, chunk, pool[node]);
                                                    pool.Served(node, clock.Elapsed, chunk.CompressedLength); break;
                                                }
                                                catch when (Fail(pool, node) && attempt < 3) { await Task.Delay(500 * (attempt + 1), cancellation); }
                                            }
                                            byte[] data = Decode(downloaded!, key.DepotKey);
                                            lock (output) { output.Position = checked((long)chunk.Offset); output.Write(data, 0, data.Length); }
                                            progress.Downloaded(data.Length);
                                            data = null!; downloaded = null;
                                            CollectDownloadBuffers();
                                        }
                                        finally { transfers.Release(); }
                                    }));
                                }
                                if (!HashMatches(partial, file.FileHash))
                                { File.Delete(partial); throw new InvalidDataException("Downloaded file hash mismatch"); }
                                if (File.Exists(path)) File.Delete(path);
                                File.Move(partial, path);
                            }
                            finally { files.Release(); }
                        }).ToArray();
                        await Task.WhenAll(tasks);
                    }
                    Console.WriteLine($"STEAM_APP_DEPOT_RESUMED files={resumed}");
                    File.WriteAllText(Path.Combine(destination, "verified-" + depot + ".json"), new JObject {
                        ["appId"] = app, ["depotId"] = depot, ["manifestId"] = manifestId.ToString(),
                        ["bytes"] = total, ["files"] = manifest.Files.Count }.ToString());
                    Console.WriteLine("STEAM_APP_DEPOT_VERIFIED " + depot);
                }
            }
        }
        // Exception filter: every failed attempt counts against its node, including the last one that rethrows.
        static bool Fail(ContentServerPool pool, int node) { pool.Failed(node); return true; }
        static bool HashMatches(string path, byte[] expected)
        {
            var digest = new byte[20];
            if (Native.bpp_steam_file_sha1(path, digest) != 0) throw new IOException("Cannot hash downloaded file");
            return digest.SequenceEqual(expected);
        }
        static byte[] Decode(DepotChunk chunk, byte[] key)
        {
            byte[] decrypted = new byte[chunk.Data.Length];
            int length = Native.bpp_steam_decrypt(chunk.Data, chunk.Data.Length, key, decrypted);
            if (length < 4) throw new InvalidDataException("Chunk decryption failed");
            byte[] result;
            if (decrypted[0] == 'V' && decrypted[1] == 'S' && decrypted[2] == 'Z' && decrypted[3] == 'a')
            {
                result = new byte[chunk.ChunkInfo.UncompressedLength];
                if (Native.bpp_steam_zstd(decrypted, length, result, result.Length) != result.Length) throw new InvalidDataException("Zstd chunk failed");
            }
            else { chunk.Process(key); result = chunk.Data; }
            if (result.Length != chunk.ChunkInfo.UncompressedLength || !CryptoHelper.AdlerHash(result).SequenceEqual(chunk.ChunkInfo.Checksum!))
                throw new InvalidDataException("Chunk checksum mismatch");
            return result;
        }
    }
}
