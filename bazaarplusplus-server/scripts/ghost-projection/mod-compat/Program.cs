using System.Net;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;

var modRoot = Path.GetFullPath(args[0]);
using (var pdb = File.OpenRead(modRoot + "/tests/ModApi.Tests/bin/Debug/net10.0/BazaarPlusPlus.ModApi.pdb"))
using (var provider = MetadataReaderProvider.FromPortablePdbStream(pdb))
{
    var reader = provider.GetMetadataReader();
    var matched = new HashSet<string>();
    var names = new[] { "GhostBattleClient.cs", "GhostBattleImportRecord.cs", "ModApiSession.cs", "ModApiResponse.cs" };
    foreach (var handle in reader.Documents)
    {
        var doc = reader.GetDocument(handle);
        var name = reader.GetString(doc.Name);
        var shortName = Path.GetFileName(name);
        if (!names.Contains(shortName)) continue;
        var relative = name[name.IndexOf("/src/", StringComparison.Ordinal)..];
        var source = File.ReadAllBytes(modRoot + relative);
        var expected = reader.GetBlobBytes(doc.Hash);
        var actual = expected.Length == 32 ? SHA256.HashData(source) : SHA1.HashData(source);
        if (!actual.SequenceEqual(expected)) throw new Exception("Compiled source mismatch: " + shortName);
        matched.Add(shortName);
    }
    if (matched.Count != names.Length) throw new Exception("Missing source checksum evidence");
    Console.WriteLine(JsonSerializer.Serialize(new { verified_source_checksums = matched }));
}

var full = JsonNode.Parse("""
{
  "battle_id":"probe-battle", "bundle_id":"01K1ABCDEF0123456789ABCDEF",
  "recorded_at_ms":1789210000000, "day":12, "hour":18,
  "encounter_id":"probe-encounter", "combat_kind":"pvp", "result":"loss",
  "winner_combatant_id":"Opponent", "loser_combatant_id":"Player", "is_final_battle":true,
  "player":{"account_id":"account-uploader","display_name":"Challenger","hero_id":"hero-one","hero_name":"Vanessa","rank":"Gold","rating":1500,"level":12,"prestige":8,"victories":7},
  "opponent":{"account_id":"account-local","display_name":"Local player","hero_id":"hero-two","hero_name":"Pygmalien","rank":"Silver","rating":1000,"level":10,"prestige":5,"victories":6},
  "download_url":"https://r2.example/probe.bundle", "download_expires_at_ms":1789800000000
}
""")!.AsObject();
var minimal = full.DeepClone().AsObject();
foreach (var name in new[] { "encounter_id", "combat_kind", "loser_combatant_id" }) minimal.Remove(name);
foreach (var side in new[] { "player", "opponent" })
{
    var part = minimal[side]!.AsObject();
    foreach (var name in new[] { "hero_id", "level", "prestige", "victories" }) part.Remove(name);
    if (side == "opponent") foreach (var name in new[] { "display_name", "rank", "rating" }) part.Remove(name);
}
var original = await Parse(full);
var compact = await Parse(minimal);
if (original.Battles.Count != 1 || compact.Battles.Count != 1) throw new Exception("Discovery row was dropped");
if (Fingerprint(original.Battles[0]) != Fingerprint(compact.Battles[0])) throw new Exception("Consumed fields changed");
Console.WriteLine(JsonSerializer.Serialize(new { compact_response_accepted = true, consumed_fields_preserved = true, full_response_bytes = System.Text.Encoding.UTF8.GetByteCount(full.ToJsonString()), compact_response_bytes = System.Text.Encoding.UTF8.GetByteCount(minimal.ToJsonString()) }));

foreach (var required in new[] { "battle_id", "bundle_id", "recorded_at_ms", "day", "hour", "download_url", "download_expires_at_ms", "player.account_id", "opponent.account_id" })
{
    var test = minimal.DeepClone().AsObject();
    var parts = required.Split('.');
    if (parts.Length == 1) test.Remove(required);
    else test[parts[0]]!.AsObject().Remove(parts[1]);
    var parsed = await Parse(test);
    if (!parsed.Succeeded || parsed.Battles.Count != 0) throw new Exception("Required field expectation failed: " + required);
    Console.WriteLine(JsonSerializer.Serialize(new { required_field = required, omission_drops_row = true }));
}
var contract = JsonNode.Parse(File.ReadAllText(args[1]))!["battles"]![0]!.AsObject();
var parsedContract = await Parse(contract);
if (parsedContract.Battles.Count != 1) throw new Exception("Server contract row was dropped");
var parsedRow = parsedContract.Battles[0];
if (parsedRow.Day != 10 || parsedRow.Hour != 18 || parsedRow.PlayerRank != "Gold" || parsedRow.PlayerRating != 1234 || parsedRow.PlayerName != "Uploader" || parsedRow.WinnerCombatantId != "Opponent" || !parsedRow.IsFinalBattle)
    throw new Exception("Server contract consumed fields changed");
Console.WriteLine(JsonSerializer.Serialize(new { server_contract_accepted = true }));
Console.WriteLine("Ghost field usage probe passed.");

static string Fingerprint(GhostBattleImportRecord x) => JsonSerializer.Serialize(new { x.BattleId,x.BundleId,x.RecordedAtUtc,x.Day,x.Hour,x.PlayerAccountId,x.OpponentAccountId,x.PlayerName,x.PlayerHero,x.OpponentHero,x.PlayerRank,x.PlayerRating,x.Result,x.WinnerCombatantId,x.IsFinalBattle,x.DownloadUrl,x.DownloadExpiresAtUtc,x.ReplayAvailable });
static async Task<GhostBattleQueryResult> Parse(JsonObject row)
{
    using var session = ModApiSession.TryCreate("https://api.example", "5.3.0", "StorageProbe", TimeSpan.FromSeconds(2), new Handler(row.ToJsonString())) ?? throw new Exception("Session creation failed");
    var result = await session.QueryGhostBattlesAgainstMeAsync("account-local", 200, CancellationToken.None);
    if (!result.Succeeded) throw new Exception("Discovery failed");
    return result;
}
sealed class Handler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"battles\":[" + json + "]}",System.Text.Encoding.UTF8,"application/json") });
}
