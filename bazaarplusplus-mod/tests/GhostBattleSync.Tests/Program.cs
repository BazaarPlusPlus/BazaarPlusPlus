#nullable enable
using System.Net;
using System.Net.Http.Headers;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;
using Microsoft.Data.Sqlite;

await DiscoveryUsesV5ShapeAndLimit();
await RetryAfterStartsCooldown();
await DownloadLimitsAndTransportErrorsStayClosed();
await ExpiredUrlRefreshesOnceAndBecomesTerminal();
await CorruptBundleBecomesPermanentWithoutRepeatedDownload();
RecorderPerspectiveManifestKeepsSides();
LegacyPayloadNormalizesOnceAndIsIdempotent();
UnknownProjectionAndCountsStayUnknown();
HistoryGhostRecoveryTests.Run();

Console.WriteLine("Ghost battle V5 sync tests passed.");

static async Task DiscoveryUsesV5ShapeAndLimit()
{
    Uri? observed = null;
    var handler = new RecordingHandler(request =>
    {
        observed = request.RequestUri;
        return JsonResponse(
            BattlePage(
                "battle-1",
                "01K1ABCDEF0123456789ABCDEF",
                "https://r2.example/bundle",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "account-uploader",
                "account-local"
            )
        );
    });
    using var session = TestModApiSessionFactory.Create(handler);
    var result = await session.QueryGhostBattlesAgainstMeAsync(
        "account-local",
        999,
        CancellationToken.None
    );

    Assert(result.Succeeded && result.Battles.Count == 1, "V5 discovery page must parse.");
    Assert(observed?.AbsolutePath == "/ghost-battles", "Discovery route must be /ghost-battles.");
    Assert(
        observed?.Query.Contains("limit=200", StringComparison.Ordinal) == true,
        "Discovery limit must clamp to 200."
    );
    Assert(
        result.Battles[0].PlayerHandItemCount == null,
        "Projection counts must remain unknown before download."
    );
    Assert(
        result.Battles[0].ReplayAvailable,
        "A valid presigned URL must expose replay availability."
    );

    var overLimit = new RecordingHandler(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
        };
        response.Content.Headers.ContentLength = 8_388_608;
        return response;
    });
    using var downloadSession = TestModApiSessionFactory.Create(overLimit);
    var tooLarge = await downloadSession.DownloadGhostBundleAsync(
        "https://r2.example/large",
        CancellationToken.None
    );
    Assert(
        !tooLarge.Succeeded && tooLarge.Error == "bundle_too_large",
        "Content-Length must be bounded before reading."
    );
}

static async Task RetryAfterStartsCooldown()
{
    using var fixture = new GhostFixture(request =>
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":\"rate_limited\",\"message\":\"private\",\"retryable\":true}}"
            ),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        return response;
    });
    var first = await fixture.Service.SyncRecentBattlesAsync(CancellationToken.None);
    var second = await fixture.Service.SyncRecentBattlesAsync(CancellationToken.None);
    Assert(
        !first.Succeeded && !second.Succeeded,
        "Rate-limited discovery must fail without throwing."
    );
    Assert(fixture.Handler.Count == 1, "Retry-After cooldown must suppress the second HTTP query.");
}

static async Task DownloadLimitsAndTransportErrorsStayClosed()
{
    var streamed = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ChunkedContent(new byte[8 * 1024 * 1024 + 1]),
    });
    using (var session = TestModApiSessionFactory.Create(streamed))
    {
        var result = await session.DownloadGhostBundleAsync(
            "https://r2.example/streamed-large",
            CancellationToken.None
        );
        Assert(!result.Succeeded && result.Error == "bundle_too_large", "stream cap");
    }

    var oversizedError = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
    {
        Content = new StringContent(new string('x', 16 * 1024 + 1)),
    });
    using (var session = TestModApiSessionFactory.Create(oversizedError))
    {
        var result = await session.DownloadGhostBundleAsync(
            "https://r2.example/error-large",
            CancellationToken.None
        );
        Assert(!result.Succeeded && result.Error == "bundle_too_large", "error body cap");
    }

    const string sentinel = "private-transport-sentinel";
    var transport = new RecordingHandler(_ => throw new HttpRequestException(sentinel));
    using (var session = TestModApiSessionFactory.Create(transport))
    {
        var result = await session.QueryGhostBattlesAgainstMeAsync(
            "account",
            10,
            CancellationToken.None
        );
        Assert(!result.Succeeded && result.Error == "transport_error", "closed transport code");
        Assert(
            result.DiagnosticException?.Message == sentinel,
            "transport exception remains diagnostic"
        );
        Assert(!result.Error!.Contains(sentinel, StringComparison.Ordinal), "no raw UI error");
    }
}

static async Task ExpiredUrlRefreshesOnceAndBecomesTerminal()
{
    var bundleId = "01K1ABCDEF0123456789ABCDEG";
    var record = CreateImport(
        "battle-expired",
        bundleId,
        "https://r2.example/expired",
        DateTimeOffset.UtcNow.AddMinutes(-1)
    );
    using var fixture = new GhostFixture(request =>
    {
        if (request.RequestUri?.AbsolutePath == "/ghost-battles")
            return JsonResponse(
                BattlePage(
                    record.BattleId,
                    bundleId,
                    "https://r2.example/refreshed",
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    "account-uploader",
                    "account-local"
                )
            );
        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("missing"),
        };
    });
    fixture.Repository.UpsertGhostBattles("account-local", [record]);
    var localId = fixture
        .Repository.ListGhostBattles(
            "account-local",
            BazaarPlusPlus.Game.HistoryPanel.GhostBattleFilter.All,
            false,
            new()
        )
        .Rows.Single()
        .BattleId;
    var replayRoot = Path.Combine(fixture.Root, "CombatReplays");

    var first = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    var callsAfterFirst = fixture.Handler.Count;
    var second = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    Assert(!first.Succeeded && !second.Succeeded, "A refreshed 404 must become expired.");
    Assert(
        callsAfterFirst == 2,
        "An expired URL must refresh discovery exactly once before download."
    );
    Assert(
        fixture.Handler.Count == callsAfterFirst,
        "An expired terminal row must not download again."
    );
    Assert(
        fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState == "expired",
        "Expired must persist."
    );
}

static async Task CorruptBundleBecomesPermanentWithoutRepeatedDownload()
{
    using var fixture = new GhostFixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent([0, 1, 2, 3]),
    });
    fixture.Repository.UpsertGhostBattles(
        "account-local",
        [
            CreateImport(
                "battle-corrupt",
                "01K1ABCDEF0123456789ABCDEH",
                "https://r2.example/corrupt",
                DateTimeOffset.UtcNow.AddMinutes(5)
            ),
        ]
    );
    var localId = fixture
        .Repository.ListGhostBattles(
            "account-local",
            BazaarPlusPlus.Game.HistoryPanel.GhostBattleFilter.All,
            false,
            new()
        )
        .Rows.Single()
        .BattleId;
    var replayRoot = Path.Combine(fixture.Root, "CombatReplays");

    var first = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    var callsAfterFirst = fixture.Handler.Count;
    var second = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    Assert(!first.Succeeded && !second.Succeeded, "Corrupt bundles must fail permanently.");
    Assert(
        fixture.Handler.Count == callsAfterFirst,
        "unavailable_payload must not download twice."
    );
    Assert(
        fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState
            == "unavailable_payload",
        "Corruption state must persist."
    );
}

static void RecorderPerspectiveManifestKeepsSides()
{
    var battle = new RunBattleV5
    {
        Facts = new BattleFactsV5
        {
            RecordedAtUtc = "2026-08-06T00:00:00+00:00",
            CombatKind = "PVPCombat",
            Result = "Win",
            WinnerCombatantId = "Player",
            LoserCombatantId = "Opponent",
        },
        Participants = new BattleParticipantsV5
        {
            Player = new BattleParticipantV5
            {
                AccountId = "U",
                DisplayName = "Uploader",
                Income = 7,
                Gold = 11,
            },
            Opponent = new BattleParticipantV5 { AccountId = "L", DisplayName = "Local" },
        },
        Snapshots = new BattleCardSnapshotsV5
        {
            CardSets =
            [
                SnapshotSet("player_hand", "A"),
                SnapshotSet("player_skills", "player-skill"),
                SnapshotSet("opponent_hand", "B"),
                SnapshotSet("opponent_skills", "opponent-skill"),
            ],
        },
    };
    var reference = new GhostBundleReference(
        "local-battle",
        "remote-battle",
        "U",
        "bundle",
        "https://r2.example/bundle",
        DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
        "available",
        "L"
    );

    var manifest = GhostManifestProjection.BuildRecorderPerspectiveManifest(
        reference,
        "run-1",
        battle
    );

    Assert(
        manifest.Snapshots.PlayerHand.Items.Single().TemplateId == "A",
        "Recorder player_hand must remain on the manifest player side."
    );
    Assert(
        manifest.Snapshots.OpponentHand.Items.Single().TemplateId == "B",
        "Recorder opponent_hand must remain on the manifest opponent side."
    );
    Assert(
        manifest.Participants.PlayerAccountId == "U",
        "The uploader must remain the manifest player."
    );
    Assert(
        manifest.Outcome.WinnerCombatantId == battle.Facts.WinnerCombatantId,
        "The manifest winner must keep the recorder combatant id."
    );
    Assert(
        manifest.Participants.PlayerIncome == 7 && manifest.Participants.PlayerGold == 11,
        "Recorder income and gold must be projected onto the manifest player."
    );
}

static void LegacyPayloadNormalizesOnceAndIsIdempotent()
{
    var payload = new GhostBattlePayload
    {
        PerspectiveVersion = 0,
        BattleManifest = new PvpBattleManifest
        {
            Participants = new PvpBattleParticipants
            {
                PlayerAccountId = "L",
                PlayerName = "Local",
                PlayerIncome = 7,
                PlayerGold = 11,
                OpponentAccountId = "U",
                OpponentName = "Uploader",
            },
            Outcome = new PvpBattleOutcome
            {
                Result = "Lost",
                WinnerCombatantId = "Opponent",
                LoserCombatantId = "Player",
            },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = Capture("B"),
                PlayerSkills = Capture("local-skill"),
                OpponentHand = Capture("A"),
                OpponentSkills = Capture("uploader-skill"),
            },
        },
    };

    var normalized = GhostBattlePayloadReader.Normalize(payload)!;

    Assert(normalized.PerspectiveVersion == 1, "Legacy payloads must be marked normalized.");
    Assert(
        normalized.BattleManifest.Participants.PlayerAccountId == "U"
            && normalized.BattleManifest.Participants.OpponentAccountId == "L",
        "Legacy participants must normalize to recorder perspective."
    );
    Assert(
        normalized.BattleManifest.Snapshots.PlayerHand.Items.Single().TemplateId == "A"
            && normalized.BattleManifest.Snapshots.OpponentHand.Items.Single().TemplateId == "B",
        "Legacy snapshots must normalize to recorder perspective."
    );
    Assert(
        normalized.BattleManifest.Outcome.Result == "Won"
            && normalized.BattleManifest.Outcome.WinnerCombatantId == "Player"
            && normalized.BattleManifest.Outcome.LoserCombatantId == "Opponent",
        "Legacy outcome must normalize to recorder perspective."
    );
    Assert(
        normalized.BattleManifest.Participants.PlayerIncome == null
            && normalized.BattleManifest.Participants.PlayerGold == null,
        "Legacy player income and gold must be missing when no opponent fields existed."
    );

    var playerHand = normalized.BattleManifest.Snapshots.PlayerHand;
    var second = GhostBattlePayloadReader.Normalize(normalized)!;
    Assert(ReferenceEquals(normalized, second), "Normalization must return the same payload.");
    Assert(
        ReferenceEquals(second.BattleManifest.Snapshots.PlayerHand, playerHand)
            && second.BattleManifest.Participants.PlayerAccountId == "U"
            && second.BattleManifest.Outcome.WinnerCombatantId == "Player",
        "A normalized payload must not be swapped again."
    );
}

static BattleCardSetV5 SnapshotSet(string label, string templateId) =>
    new()
    {
        Label = label,
        Status = "Captured",
        Source = "Runtime",
        Items = [new BattleCardV5 { InstanceId = templateId, TemplateId = templateId }],
    };

static PvpBattleCardSetCapture Capture(string templateId) =>
    new()
    {
        Items = [new PvpBattleCardSnapshot { InstanceId = templateId, TemplateId = templateId }],
    };

static void UnknownProjectionAndCountsStayUnknown()
{
    Assert(
        GhostBattleLocalProjector.ProjectResultToLocal("unknown") == null,
        "Unknown server result must map to the local unknown result."
    );
    var record = CreateImport(
        "battle-unknown",
        "01K1ABCDEF0123456789ABCDEJ",
        "https://r2.example/unknown",
        DateTimeOffset.UtcNow.AddMinutes(5)
    );
    using var fixture = new GhostFixture(_ => JsonResponse("{\"battles\":[]}"));
    fixture.Repository.UpsertGhostBattles("account-local", [record]);
    var local = fixture
        .Repository.ListGhostBattles(
            "account-local",
            BazaarPlusPlus.Game.HistoryPanel.GhostBattleFilter.All,
            false,
            new()
        )
        .Rows.Single();
    Assert(
        !local.SnapshotCounts.Known,
        "Hand and skill counts must be unknown before replay download."
    );
}

static GhostBattleImportRecord CreateImport(
    string battleId,
    string bundleId,
    string url,
    DateTimeOffset expiry
) =>
    new()
    {
        BattleId = battleId,
        BundleId = bundleId,
        DownloadUrl = url,
        DownloadExpiresAtUtc = expiry,
        RecordedAtUtc = DateTimeOffset.UtcNow,
        Day = 1,
        Hour = 2,
        PlayerAccountId = "account-uploader",
        PlayerName = "Uploader",
        OpponentAccountId = "account-local",
        OpponentName = "Local",
        CombatKind = "PVPCombat",
        Result = "unknown",
    };

static string BattlePage(
    string battleId,
    string bundleId,
    string url,
    DateTimeOffset expiry,
    string uploader,
    string opponent
) =>
    System.Text.Json.JsonSerializer.Serialize(
        new Dictionary<string, object?>
        {
            ["battles"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["battle_id"] = battleId,
                    ["bundle_id"] = bundleId,
                    ["recorded_at_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["download_url"] = url,
                    ["download_expires_at_ms"] = expiry.ToUnixTimeMilliseconds(),
                    ["day"] = 1,
                    ["hour"] = 2,
                    ["combat_kind"] = "PVPCombat",
                    ["result"] = "unknown",
                    ["is_final_battle"] = false,
                    ["player"] = new Dictionary<string, object?>
                    {
                        ["account_id"] = uploader,
                        ["display_name"] = "Uploader",
                    },
                    ["opponent"] = new Dictionary<string, object?>
                    {
                        ["account_id"] = opponent,
                        ["display_name"] = "Local",
                    },
                },
            },
        }
    );

static HttpResponseMessage JsonResponse(string json) =>
    new(HttpStatusCode.OK) { Content = new StringContent(json) };

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    public int Count { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Count++;
        return Task.FromResult(_respond(request));
    }
}

internal sealed class ChunkedContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(bytes, 0, bytes.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

internal static class TestModApiSessionFactory
{
    public static ModApiSession Create(HttpMessageHandler handler) =>
        ModApiSession.TryCreate(
            "https://api.example",
            "test",
            "OnlineClient",
            TimeSpan.FromSeconds(120),
            handler
        ) ?? throw new InvalidOperationException("test Mod API session");
}

internal sealed class GhostFixture : IDisposable
{
    private readonly ModApiSession _session;

    public GhostFixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        Root = Path.Combine(Path.GetTempPath(), $"bpp-ghost-v5-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        Repository = new HistoryPanelRepository(Path.Combine(Root, "runs.sqlite3"));
        Handler = new RecordingHandler(respond);
        _session = TestModApiSessionFactory.Create(Handler);
        Service = new GhostBattleSyncService(Repository, _session, () => "account-local");
    }

    public string Root { get; }
    public HistoryPanelRepository Repository { get; }
    public RecordingHandler Handler { get; }
    public GhostBattleSyncService Service { get; }

    public void Dispose()
    {
        _session.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
