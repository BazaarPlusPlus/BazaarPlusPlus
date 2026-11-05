using System;
using System.IO;
using System.Text;
using System.Linq;
using BazaarPlusPlus.Ios.Service;
using SteamKit2.Authentication;
var root = Path.Combine(Path.GetTempPath(), "steam-path-test-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try {
    var cdnDirectory = new SteamKit2.Internal.CContentServerDirectory_GetServersForSteamPipe_Response();
    SteamKit2.Internal.CContentServerDirectory_ServerInfo Node(string host, string tls) =>
        new() { vhost = host, https_support = tls };
    cdnDirectory.servers.Add(Node("optional.example.com", "optional"));
    cdnDirectory.servers.Add(Node("mandatory.example.com", "mandatory"));
    cdnDirectory.servers.Add(Node("plain.example.com", "none"));
    cdnDirectory.servers.Add(Node("unknown.example.com", "future-value"));
    var proxy = Node("proxy.example.com", "mandatory"); proxy.use_as_proxy = true; cdnDirectory.servers.Add(proxy);
    var restricted = Node("restricted.example.com", "optional"); restricted.allowed_app_ids.Add(2); cdnDirectory.servers.Add(restricted);
    var china = Node("china.example.com", "optional"); china.steam_china_only = true; cdnDirectory.servers.Add(china);
    cdnDirectory.servers.Add(Node("https://invalid.example.com/path", "mandatory"));
    var endpoints = ContentServers.Select(cdnDirectory, 1);
    if (endpoints.Length != 2 || Array.Exists(endpoints, s => s.Port != 443 || s.Protocol != SteamKit2.CDN.Server.ConnectionProtocol.HTTPS))
        throw new Exception("TLS capability or CDN restrictions lost");
    restricted.allowed_app_ids.Add(1);
    if (ContentServers.Select(cdnDirectory, 1).Length != 3) throw new Exception("Owned app CDN rejected");
    var preferred = Node("preferred.example.com", "optional"); preferred.preferred_server = true; preferred.weighted_load = 90;
    cdnDirectory.servers.Add(preferred);
    if (ContentServers.Select(cdnDirectory, 1)[0].Host != "preferred.example.com") throw new Exception("Preferred CDN node not tried first");
    Console.WriteLine("PASS: optional TLS accepted; HTTP, unknown, proxy, regional and app restrictions enforced; preferred nodes first.");

    // Faster nodes take more of the chunks in flight; a failing node drops behind.
    var pool = new ContentServerPool(new SteamKit2.CDN.Server[] { new System.Net.DnsEndPoint("fast", 443), new System.Net.DnsEndPoint("slow", 443) });
    for (int i = 0; i < 5; ++i)
    {
        int a = pool.Rent(), b = pool.Rent();
        pool.Served(a, TimeSpan.FromSeconds(a == 0 ? 0.1 : 2), 1 << 20); pool.Served(b, TimeSpan.FromSeconds(b == 0 ? 0.1 : 2), 1 << 20);
    }
    var rented = Enumerable.Range(0, 10).Select(_ => pool.Rent()).ToList();
    if (rented.Count(n => n == 0) < 8 || !rented.Contains(1)) throw new Exception("CDN pool ignores measured speed or starves the slow node");
    foreach (int node in rented) pool.Served(node, TimeSpan.FromSeconds(node == 0 ? 0.1 : 2), 1 << 20);
    int failures = 0;
    for (int node; (node = pool.Rent()) == 0 && failures < 4; ++failures) pool.Failed(node);
    if (failures == 0 || failures > 3) throw new Exception("CDN pool drops a fast node on one failure or keeps a failing one first");
    Console.WriteLine("PASS: CDN pool favours measured fast nodes, keeps slow ones in use and backs off failing ones.");
    if (AuthTickets.Crc(Encoding.ASCII.GetBytes("123456789")) != 0xcbf43926) throw new Exception("Ticket CRC does not match IEEE CRC32");
    var composedTicket = AuthTickets.ComposeWebApiTicket(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
    if (!composedTicket.SequenceEqual(new byte[] { 1, 2, 3, 2, 0, 0, 0, 4, 5 })) throw new Exception("Web API ticket must be exactly its parts, with no padding");
    try { AuthTickets.ComposeWebApiTicket(new byte[2557], new byte[0]); throw new Exception("Oversized Web API ticket accepted"); } catch (InvalidDataException) { }
    if (DownloadPaths.Resolve(root,"game/Data/file") != Path.Combine(root,"game/Data/file")) throw new Exception("Nested path rejected");
    foreach(var bad in new[]{"../escape", "game/../../escape", "/absolute", "game/./file", "game//file", "game\\..\\file", "bad\0path"}) {
        try { DownloadPaths.Resolve(root,bad); throw new Exception("Accepted unsafe path: " + bad); }
        catch(InvalidDataException) { }
    }
    string outside = Path.Combine(root,"outside"); Directory.CreateDirectory(outside);
    Directory.CreateSymbolicLink(Path.Combine(root,"link"), outside);
    try { DownloadPaths.Resolve(root,"link/file"); throw new Exception("Accepted existing symlink"); }
    catch(InvalidDataException) { }
    File.CreateSymbolicLink(Path.Combine(root,"dangling"), Path.Combine(root,"missing-target"));
    try { DownloadPaths.Resolve(root,"dangling"); throw new Exception("Accepted dangling symlink"); }
    catch(InvalidDataException) { }
    Console.WriteLine("PASS: manifest traversal, absolute paths and symlinks rejected; ticket CRC matches standard vector.");

    // A resumed download keeps chunks whose checksum matches and refetches the rest.
    var written = new byte[3000]; new Random(1).NextBytes(written);
    SteamKit2.DepotManifest.ChunkData Chunk(int offset, int length) => new() {
        Offset = (ulong)offset, UncompressedLength = (uint)length,
        Checksum = SteamKit2.CryptoHelper.AdlerHash(written.AsSpan(offset, length).ToArray()) };
    var partialChunks = new[] { Chunk(2000, 1000), Chunk(0, 1000), Chunk(1000, 1000) };
    var interrupted = (byte[])written.Clone(); Array.Clear(interrupted, 1000, 1000);
    var refetch = PartialFile.Missing(new MemoryStream(interrupted), partialChunks, SteamKit2.CryptoHelper.AdlerHash);
    if (refetch.Count != 1 || refetch[0].Offset != 1000) throw new Exception("Resume kept a missing chunk or dropped a written one");
    if (PartialFile.Missing(new MemoryStream(written, 0, 2500), partialChunks, SteamKit2.CryptoHelper.AdlerHash).Count != 1) throw new Exception("Resume kept a truncated chunk");
    Console.WriteLine("PASS: resumed downloads keep written chunks and refetch missing or truncated ones.");

    // A resumed download skips the files it already hashed, unless one has changed or the manifest moved on.
    string ledgerRoot = Path.Combine(root, "ledger"); Directory.CreateDirectory(ledgerRoot);
    string ledgerFile = Path.Combine(ledgerRoot, "game.bin"); File.WriteAllText(ledgerFile, "hashed");
    var ledger = new DownloadLedger(ledgerRoot, 1, 10);
    if (ledger.Holds("game.bin", ledgerFile)) throw new Exception("Ledger holds a file nobody hashed");
    ledger.Add("game.bin", ledgerFile);
    if (!new DownloadLedger(ledgerRoot, 1, 10).Holds("game.bin", ledgerFile)) throw new Exception("Ledger lost a file across a restart");
    File.WriteAllText(ledgerFile, "hashed, then changed");
    if (new DownloadLedger(ledgerRoot, 1, 10).Holds("game.bin", ledgerFile)) throw new Exception("Ledger trusts a changed file");
    File.WriteAllText(ledgerFile, "hashed"); File.SetLastWriteTimeUtc(ledgerFile, new DateTime(2026, 1, 1));
    new DownloadLedger(ledgerRoot, 1, 10).Add("game.bin", ledgerFile);
    if (new DownloadLedger(ledgerRoot, 1, 11).Holds("game.bin", ledgerFile)) throw new Exception("Ledger crossed manifests");
    if (new DownloadLedger(ledgerRoot, 1, 10).Holds("game.bin", ledgerFile)) throw new Exception("A new manifest kept the old ledger");
    Console.WriteLine("PASS: the download ledger skips hashed files and drops changed ones and old manifests.");

    // The bar never goes back across a retry; the speed is steady, and falls to zero when data stops.
    var now = new DateTime(2026, 1, 1); var reports = new System.Collections.Generic.List<(string state, string message, long done, long speed)>();
    var counter = new DownloadProgress((state, message, _, done, _, speed) => reports.Add((state, message, done, speed)), () => now);
    counter.Begin(1000); counter.Verified(100); counter.Tick();
    if (reports[^1].state != "verifying" || reports[^1].done != 100) throw new Exception("Verification not reported");
    for (int second = 0; second < 5; ++second) { now = now.AddSeconds(1); counter.Downloaded(100); counter.Tick(); }
    if (reports[^1].state != "downloading" || reports[^1].speed != 100 || reports[^1].done != 600) throw new Exception("Wrong speed or count: " + reports[^1]);
    counter.Begin(1000); counter.Verified(200); counter.Tick();
    if (reports[^1].done != 600) throw new Exception("The bar went back on a retry");
    counter.Stage("网络中断，正在重试");
    if (reports[^1].done != 600 || reports[^1].message != "网络中断，正在重试") throw new Exception("A stage lost the place");
    now = now.AddSeconds(7); counter.Tick();
    if (reports[^1].speed != 0 || reports[^1].message != "网络不稳定，正在等待恢复") throw new Exception("A stalled download still reports speed");
    counter.Begin(2000); counter.Tick();
    if (reports[^1].done != 0) throw new Exception("A new manifest kept the old place");
    Console.WriteLine("PASS: download progress never goes back, keeps a steady speed and reports a stall.");

    // Only Steam refusing a saved login forgets it; a bad connection or a full disk are told apart.
    AuthenticationException Auth(SteamKit2.EResult result) => new AuthenticationException("test", result);
    if (!Failures.IsTransient(Auth(SteamKit2.EResult.TryAnotherCM)) || Failures.IsRejectedLogin(SteamKit2.EResult.TryAnotherCM)) throw new Exception("A busy Steam server forgets the login");
    if (Failures.IsTransient(Auth(SteamKit2.EResult.InvalidPassword)) || !Failures.IsRejectedLogin(SteamKit2.EResult.InvalidPassword)) throw new Exception("A rejected login retried forever");
    if (Failures.IsRejectedLogin(SteamKit2.EResult.RateLimitExceeded) || Failures.IsTransient(Auth(SteamKit2.EResult.RateLimitExceeded))) throw new Exception("Rate limiting handled wrongly");
    if (!Failures.IsTransient(new IOException("network")) || !Failures.IsTransient(new System.Net.Http.HttpRequestException()) || !Failures.IsTransient(new OperationCanceledException())) throw new Exception("A network failure not retried");
    if (Failures.IsTransient(new InvalidDataException())) throw new Exception("Bad data retried");
    var full = new IOException("Disk full"); full.HResult = unchecked((int)0x80070070);
    if (Failures.IsTransient(full) || !Failures.IsDiskFull(full)) throw new Exception("A full disk retried forever");
    Console.WriteLine("PASS: transient failures retry; rejected logins and full disks stop.");

    // Each installation signs in with its own login ID, so a second device does not log out the first.
    if (InstallationIdentity.LoginIdFor("a") != InstallationIdentity.LoginIdFor("a") || InstallationIdentity.LoginIdFor("a") == InstallationIdentity.LoginIdFor("b") || InstallationIdentity.LoginIdFor("a") == 0)
        throw new Exception("Login ID must be stable, per installation and non-zero");
    Console.WriteLine("PASS: login IDs are stable and differ per installation.");

    // A relocated runtime must include the iOS player's resources, not only game assets.
    string assets = Path.Combine(root, "app");
    string bundle = Path.Combine(root, "content/1/Game.app");
    string source = Path.Combine(bundle, "Contents/Resources/Data");
    string target = Path.Combine(root, "runtimes/test-game/Data");
    foreach (string directory in new[] { Path.Combine(source,"Managed"), Path.Combine(assets,"Data/Managed"),
        Path.Combine(assets,"Data/Resources"), Path.Combine(assets,"Mods"), Path.Combine(target,"Raw") }) Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(bundle,"Contents/Info.plist"), "<plist><dict><key>CFBundleShortVersionString</key><string>1.0</string></dict></plist>");
    File.WriteAllText(Path.Combine(source,"globalgamemanagers"), "6000.3.11f1\0game-assets");
    File.WriteAllText(Path.Combine(source,"ScriptingAssemblies.json"), "{\"names\":[],\"types\":[]}");
    File.WriteAllText(Path.Combine(source,"RuntimeInitializeOnLoads.json"), "{\"root\":[]}");
    File.WriteAllText(Path.Combine(source,"Managed/mscorlib.dll"), "corlib");
    File.WriteAllText(Path.Combine(assets,"Data/Managed/mscorlib.dll"), "corlib");
    File.WriteAllText(Path.Combine(source,"Managed/UnityEngine.CoreModule.dll"), "mac-engine");
    File.WriteAllText(Path.Combine(assets,"Data/Managed/UnityEngine.CoreModule.dll"), "ios-engine");
    File.WriteAllText(Path.Combine(assets,"Data/Resources/unity default resources"), "ios-builtins");
    File.WriteAllText(Path.Combine(assets,"Data/boot.config"), "ios-boot");
    File.WriteAllText(Path.Combine(assets,"BazaarPlusPlus.Ios.Helper.dll"), "helper");
    File.WriteAllText(Path.Combine(assets,"Mods/0Harmony.dll"), "harmony");
    File.WriteAllText(Path.Combine(assets,"entrypoints.json"), "[]");
    var profile = Newtonsoft.Json.Linq.JObject.Parse("{\"id\":\"test-game\",\"steam\":{\"appId\":1},\"installPath\":\"Game.app\",\"testedGameVersion\":\"1.0\",\"dataPath\":\"Contents/Resources/Data\",\"unityVersion\":\"6000.3.11f1\"}");
    typeof(DownloadPaths).Assembly.GetType("BazaarPlusPlus.Ios.Service.Installer")!.GetMethod("Prepare")!
        .Invoke(null, new object[] { profile, root, assets, System.Threading.CancellationToken.None });
    foreach (var pair in new[] { ("Resources/unity default resources", "ios-builtins"),
        ("boot.config", "ios-boot"), ("Managed/UnityEngine.CoreModule.dll", "ios-engine"),
        ("globalgamemanagers", "6000.3.11f1\0game-assets"), ("Managed/0Harmony.dll", "harmony") })
        if (File.ReadAllText(Path.Combine(target, pair.Item1)) != pair.Item2) throw new Exception("Wrong runtime content: " + pair.Item1);
    Console.WriteLine("PASS: relocated game runtime includes iOS built-ins, boot settings, engine assemblies and mod libraries.");
    if (GameDefinition.InstalledVersion(root, profile) != "1.0") throw new Exception("Installed version not read from the download");
    var newer = (Newtonsoft.Json.Linq.JObject)profile.DeepClone(); newer["testedGameVersion"] = "0.9";
    void Prepare(Newtonsoft.Json.Linq.JObject game) => typeof(DownloadPaths).Assembly.GetType("BazaarPlusPlus.Ios.Service.Installer")!.GetMethod("Prepare")!
        .Invoke(null, new object[] { game, root, assets, System.Threading.CancellationToken.None });
    Prepare(newer);
    Console.WriteLine("PASS: a game newer than the tested one on the same engine prepares.");
    void ExpectEngineChanged(Newtonsoft.Json.Linq.JObject game)
    {
        try { Prepare(game); throw new Exception("Changed engine prepared"); }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is GameEngineChangedException changed && changed.Found == "1.0") { }
    }
    File.WriteAllText(Path.Combine(source,"Managed/mscorlib.dll"), "other-corlib");
    ExpectEngineChanged(newer);
    File.WriteAllText(Path.Combine(source,"Managed/mscorlib.dll"), "corlib");
    var otherUnity = (Newtonsoft.Json.Linq.JObject)newer.DeepClone(); otherUnity["unityVersion"] = "6000.4.0f1";
    ExpectEngineChanged(otherUnity);
    var corrupt = (Newtonsoft.Json.Linq.JObject)profile.DeepClone(); corrupt["unityVersion"] = "6000.4.0f1";
    try { Prepare(corrupt); throw new Exception("Wrong engine at the tested version prepared"); }
    catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
    Console.WriteLine("PASS: a changed Unity version or corlib stops a newer game with the version found; at the tested version it is corruption.");
    // A rebuilt game keeps the derived copy; a corrupted bundled patch must fail closed before replacement.
    string HashFile(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    string original = Path.Combine(source, "Managed/FMODUnity.dll");
    string adapterFolder = Path.Combine(assets, "GameAdapters");
    Directory.CreateDirectory(adapterFolder);
    string patch = Path.Combine(adapterFolder, "FMODUnity.dll");
    File.WriteAllText(original, "original-fmod");
    File.WriteAllText(patch, "derived-static-fmod");
    profile["managedAdapters"] = Newtonsoft.Json.Linq.JArray.Parse(
        "[{\"assembly\":\"FMODUnity.dll\",\"adapter\":\"test-adapter\",\"sourceSha256\":\"" + HashFile(original) + "\"}]");
    var receipt = new Newtonsoft.Json.Linq.JObject {
        ["gameId"]="test-game", ["assembly"]="FMODUnity.dll", ["adapter"]="test-adapter",
        ["sourceSha256"]=HashFile(original), ["patchedSha256"]=HashFile(patch)
    };
    File.WriteAllText(Path.Combine(adapterFolder,"manifest.json"), receipt.ToString());
    var applyAdapter = typeof(DownloadPaths).Assembly.GetType("BazaarPlusPlus.Ios.Service.ManagedAdapters")!.GetMethod("Apply")!;
    void ApplyAdapter() => applyAdapter.Invoke(null, new object[] { profile, Path.Combine(source,"Managed"), Path.Combine(target,"Managed"), assets });
    ApplyAdapter();
    if (File.ReadAllText(original) != "original-fmod" ||
        File.ReadAllText(Path.Combine(target,"Managed/FMODUnity.dll")) != "derived-static-fmod")
        throw new Exception("Adapter modified original or failed to replace derived copy");
    File.WriteAllText(original, "rebuilt-fmod");
    File.Delete(Path.Combine(target,"Managed/FMODUnity.dll"));
    ApplyAdapter();
    if (File.ReadAllText(original) != "rebuilt-fmod" ||
        File.ReadAllText(Path.Combine(target,"Managed/FMODUnity.dll")) != "derived-static-fmod")
        throw new Exception("A rebuilt game did not keep the derived copy");
    File.WriteAllText(patch, "corrupt-patch");
    try { ApplyAdapter(); throw new Exception("Accepted a corrupt patch"); }
    catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
    File.WriteAllText(patch, "derived-static-fmod");
    receipt["gameId"]="another-game";
    File.WriteAllText(Path.Combine(adapterFolder,"manifest.json"), receipt.ToString());
    try { ApplyAdapter(); throw new Exception("Accepted another game's adapter"); }
    catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { }
    Console.WriteLine("PASS: managed adapters preserve originals, survive a rebuilt game and reject corrupt patches and cross-game receipts.");
    var game = Newtonsoft.Json.Linq.JObject.Parse("""{"id":"first","displayName":"First","steam":{"appId":1,"macDepots":[2]},"installPath":"Game.app","dataPath":"Contents/Resources/Data","unityVersion":"6000.3.11f1","testedGameVersion":"1.0","library":{"platforms":{"simulator":{"launch":true},"device":{"launch":false}}}}""");
    var definitionPath = Path.Combine(assets, "game.json");
    File.WriteAllText(definitionPath, game.ToString());
    if (!new GameDefinition(assets, "simulator").CanLaunch || new GameDefinition(assets, "device").CanLaunch) throw new Exception("Platform launch gate ignored");
    try { new GameDefinition(assets, "desktop"); throw new Exception("Unknown platform accepted"); } catch(InvalidDataException) { }
    Directory.CreateDirectory(Path.Combine(root,"content/1"));
    File.WriteAllText(Path.Combine(root,"content/1/verified-2.json"), """{"appId":1,"depotId":2,"files":10}""");
    if (!GameDefinition.IsDownloaded(root,game)) throw new Exception("Verified download not recognized");
    File.WriteAllText(Path.Combine(root,"content/1/verified-2.json"), """{"appId":3,"depotId":2,"files":10}""");
    if (GameDefinition.IsDownloaded(root,game)) throw new Exception("Receipt for another app accepted");
    File.WriteAllText(Path.Combine(root,"content/1/verified-2.json"), "{broken");
    if (GameDefinition.IsDownloaded(root,game)) throw new Exception("Malformed receipt accepted");
    game["id"] = "../escape";
    File.WriteAllText(definitionPath, game.ToString());
    try { new GameDefinition(assets,"simulator"); throw new Exception("Unsafe game ID accepted"); } catch(InvalidDataException) { }
    Console.WriteLine("PASS: game definition validation, platform capability gates and download receipts.");
} finally { Directory.Delete(root,true); }
