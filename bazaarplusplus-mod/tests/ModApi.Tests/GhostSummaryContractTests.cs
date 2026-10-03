#nullable enable
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.ModApi.Clients;

internal static class GhostSummaryContractTests
{
    public static async Task RunAsync()
    {
        string golden;
        using (
            var stream =
                Assembly
                    .GetExecutingAssembly()
                    .GetManifestResourceStream("ModApi.Tests.ghost-summary.response.json")
                ?? throw new InvalidOperationException(
                    "The server-owned Ghost summary contract is missing."
                )
        )
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            golden = reader.ReadToEnd();

        // The server golden owns download_url; compare against it so a signed URL lands
        // as a server-only golden change.
        string goldenDownloadUrl;
        using (var contract = JsonDocument.Parse(golden))
            goldenDownloadUrl = contract
                .RootElement.GetProperty("battles")[0]
                .GetProperty("download_url")
                .GetString()!;

        using var session =
            ModApiSession.TryCreate(
                "https://api.example",
                "1.0.0",
                "GhostSummaryContract",
                TimeSpan.FromSeconds(30),
                new GoldenHandler(golden)
            ) ?? throw new InvalidOperationException("valid session");
        var result = await session.QueryGhostBattlesAgainstMeAsync(
            "account-local",
            200,
            CancellationToken.None
        );

        Assert(result.Succeeded, $"Ghost discovery failed: {result.Error}");
        Assert(
            result.Battles.Count == 1,
            $"Ghost discovery kept {result.Battles.Count} rows from the server contract, expected 1."
        );
        var row = result.Battles[0];
        Expect("BattleId", row.BattleId, "battle-001");
        Expect("BundleId", row.BundleId, "01J00000000000000000000801");
        Expect(
            "RecordedAtUtc",
            row.RecordedAtUtc,
            DateTimeOffset.FromUnixTimeMilliseconds(1789209999000)
        );
        Expect("Day", row.Day, 10);
        Expect("Hour", row.Hour, 18);
        Expect("Result", row.Result, "loss");
        Expect("WinnerCombatantId", row.WinnerCombatantId, "Opponent");
        Expect("IsFinalBattle", row.IsFinalBattle, true);
        Expect("PlayerAccountId", row.PlayerAccountId, "account-uploader");
        Expect("PlayerName", row.PlayerName, "Uploader");
        Expect("PlayerHero", row.PlayerHero, "Vanessa");
        Expect("PlayerRank", row.PlayerRank, "Gold");
        Expect("PlayerRating", row.PlayerRating, 1234);
        Expect("OpponentAccountId", row.OpponentAccountId, "account-local");
        Expect("OpponentHero", row.OpponentHero, "Pygmalien");
        Expect(
            "DownloadExpiresAtUtc",
            row.DownloadExpiresAtUtc,
            DateTimeOffset.FromUnixTimeMilliseconds(1789814800000)
        );
        Expect("DownloadUrl", row.DownloadUrl, goldenDownloadUrl);
        Expect("ReplayAvailable", row.ReplayAvailable, true);
        Console.WriteLine("GhostSummaryContractTests passed (server golden, 18 consumed fields).");
    }

    private static void Expect<T>(string field, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new InvalidOperationException(
                $"Ghost summary contract field {field} parsed as '{actual}', expected '{expected}'."
            );
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class GoldenHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
    }
}
