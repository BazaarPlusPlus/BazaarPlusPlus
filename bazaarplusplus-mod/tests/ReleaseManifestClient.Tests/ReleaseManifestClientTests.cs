using System.Net;
using System.Text.Json;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.TestSupport;
using Xunit;

// Not ReleaseManifestClient.Tests: that namespace would hide the Core type of the same name.
namespace ReleaseManifest.Tests;

// Each test is one check of the capsule it replaces, with the same calls and messages.
public sealed class ReleaseManifestClientTests
{
    [Fact]
    public async Task Shared_release_manifest_yields_the_product_version()
    {
        var body = TestInputs.Scratch(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "latest.json")
        );
        using var fixture = JsonDocument.Parse(body);
        var result = await Fetch(_ => Response(HttpStatusCode.OK, body));

        True(result.Succeeded, "The shared Product Release fixture should succeed.");
        Equal(
            fixture.RootElement.GetProperty("version").GetString(),
            result.Version,
            "The mod should read the product version from the writer's complete manifest."
        );
    }

    [Fact]
    public async Task Shared_platform_release_manifests_yield_their_managed_payload()
    {
        foreach (
            var (platformKey, expectedVersion) in new[]
            {
                (ReleaseManifestEndpoints.WindowsPlatformKey, "3.1.2"),
                (ReleaseManifestEndpoints.MacPlatformKey, "3.1.1"),
                (ReleaseManifestEndpoints.AndroidPlatformKey, "3.1.2"),
            }
        )
        {
            var body = TestInputs.Scratch(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "latest", $"{platformKey}.json")
            );
            using var fixture = JsonDocument.Parse(body);
            using var http = new HttpClient(
                new Handler(request =>
                {
                    Equal(
                        $"https://cdn.bazaarplusplus.com/installer/latest/mod/{platformKey}.json",
                        request.RequestUri!.ToString(),
                        "The mod requests its own update endpoint."
                    );
                    return Response(HttpStatusCode.OK, body);
                })
            );
            var result = await new ReleaseManifestClient(
                http,
                ReleaseManifestEndpoints.ForPlatformKey(platformKey),
                platformKey
            ).FetchAsync(default);

            True(
                result.Succeeded,
                $"The shared {platformKey} Platform Release Manifest should succeed."
            );
            var release = fixture.RootElement.GetProperty("downloads").GetProperty(platformKey);
            var payload = release.GetProperty("managedPayload");
            True(
                result.ManagedPayload != null,
                $"The {platformKey} manifest should yield its Managed Payload."
            );
            Equal(
                expectedVersion,
                result.ManagedPayload!.Version,
                "The payload is that version's."
            );
            Equal(
                payload.GetProperty("url").GetString(),
                result.ManagedPayload.Url.ToString(),
                "The payload URL is the writer's."
            );
            Equal(payload.GetProperty("size").GetInt64(), result.ManagedPayload.Size, "Size.");
            Equal(
                payload.GetProperty("sha256").GetString(),
                result.ManagedPayload.Sha256,
                "SHA-256."
            );
            Equal(
                payload.GetProperty("signature").GetString(),
                result.ManagedPayload.Signature,
                "Signature."
            );
            Equal(
                release.GetProperty("historyDatabaseUserVersion").GetInt32(),
                result.ManagedPayload.HistoryDatabaseUserVersion,
                "History database schema."
            );
            Equal(
                release.GetProperty("nonManagedPayloadDigest").GetString(),
                result.ManagedPayload.NonManagedPayloadDigest,
                "Non-managed payload digest."
            );
            Equal(
                expectedVersion,
                fixture.RootElement.GetProperty("version").GetString(),
                $"The shared {platformKey} fixture should carry its own platform version."
            );
            Equal(
                expectedVersion,
                result.Version,
                $"The mod should read the {platformKey} version from that platform's manifest."
            );
        }
    }

    [Fact]
    public async Task Android_app_is_read_only_when_complete()
    {
        var body = TestInputs.Scratch(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "latest", "android-aarch64.json")
        );
        using var fixture = JsonDocument.Parse(body);
        var release = fixture
            .RootElement.GetProperty("downloads")
            .GetProperty(ReleaseManifestEndpoints.AndroidPlatformKey);
        var result = await Fetch(
            _ => Response(HttpStatusCode.OK, body),
            platformKey: ReleaseManifestEndpoints.AndroidPlatformKey
        );

        True(
            result.AndroidApp != null,
            "The Android manifest should yield its Android App release."
        );
        var adaptations = release.GetProperty("adaptations").EnumerateObject().ToList();
        Equal(
            "play publisher",
            string.Join(" ", result.AndroidApp!.Games.Select(game => game.Source).Order()),
            "The release is adapted for one game from each Android source."
        );
        Equal(adaptations.Count, result.AndroidApp.Games.Count, "Every adaptation is read.");
        foreach (var (adaptation, game) in adaptations.Zip(result.AndroidApp.Games))
        {
            var written = adaptation.Value.GetProperty("game");
            Equal(adaptation.Name, game.Source, "Each game is named by its Android source.");
            Equal(
                written.GetProperty("gameVersion").GetString(),
                game.GameVersion,
                "Each adapted game's version is the writer's."
            );
            Equal(
                written.GetProperty("versionCode").GetInt64(),
                game.VersionCode,
                "Each adapted game's versionCode is the writer's."
            );
            Equal(
                written.GetProperty("il2cppSha256").GetString(),
                game.Il2CppSha256,
                "Each adapted game's il2cpp hash is the writer's."
            );
            Equal(
                written.GetProperty("metadataSha256").GetString(),
                game.MetadataSha256,
                "Each adapted game's metadata hash is the writer's."
            );
        }
        Equal(
            release.GetProperty("app").GetProperty("url").GetString(),
            result.AndroidApp.AppUrl.ToString(),
            "The App URL is the writer's."
        );
        Equal(
            release.GetProperty("app").GetProperty("size").GetInt64(),
            result.AndroidApp.AppSize,
            "The App size is the writer's."
        );
        Equal(
            release.GetProperty("app").GetProperty("sha256").GetString(),
            result.AndroidApp.AppSha256,
            "The App SHA-256 is the writer's."
        );

        var desktop = await Fetch(
            _ => Response(HttpStatusCode.OK, body),
            platformKey: ReleaseManifestEndpoints.MacPlatformKey
        );
        True(desktop.AndroidApp == null, "Another platform's record names no Android App.");

        // Each record below is complete but for the one defect its name says.
        True(
            await ReadAndroidApp(AndroidRecord(App, Adaptations(("publisher", Game)))) != null,
            "A complete record is a release."
        );
        True(
            await ReadAndroidApp(AndroidRecord(null, Adaptations(("publisher", Game)))) == null,
            "A record without an App names no Android App."
        );
        True(
            await ReadAndroidApp(AndroidRecord(App, adaptations: null, game: Game)) == null,
            "A record with only the top-level game, which older mods read, names no adapted game."
        );
        True(
            await ReadAndroidApp(AndroidRecord(App, "{}")) == null,
            "A record that adapts no game is no release."
        );
        True(
            await ReadAndroidApp(
                AndroidRecord(App.Replace("https://", "http://"), Adaptations(("publisher", Game)))
            ) == null,
            "An App link that is not HTTPS is never offered."
        );
        True(
            await ReadAndroidApp(
                AndroidRecord(
                    """{"url":"https://cdn.example/a.apk","size":1}""",
                    Adaptations(("publisher", Game))
                )
            ) == null,
            "An App a download cannot check is no release."
        );
        True(
            await ReadAndroidApp(
                AndroidRecord(
                    App,
                    Adaptations(
                        (
                            "publisher",
                            """{"versionCode":13000,"il2cppSha256":"1111111111111111111111111111111111111111111111111111111111111111","metadataSha256":"2222222222222222222222222222222222222222222222222222222222222222"}"""
                        )
                    )
                )
            ) == null,
            "A game without its version is no release."
        );
        True(
            await ReadAndroidApp(
                AndroidRecord(
                    App,
                    Adaptations(
                        (
                            "publisher",
                            """{"gameVersion":"1.0.13000-production-android-0000000a","versionCode":13000}"""
                        )
                    )
                )
            ) == null,
            "A game without its code hashes is no release."
        );
        True(
            await ReadAndroidApp(
                AndroidRecord(
                    App,
                    Adaptations(
                        ("publisher", Game),
                        (
                            "play",
                            """{"gameVersion":"1.0.6","il2cppSha256":"3333333333333333333333333333333333333333333333333333333333333333","metadataSha256":"4444444444444444444444444444444444444444444444444444444444444444"}"""
                        )
                    )
                )
            ) == null,
            "One incomplete adaptation beside a complete one is no release."
        );
    }

    private const string App = """
        {"url":"https://cdn.example/a.apk","size":1,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}
        """;

    private const string Game = """
        {"gameVersion":"1.0.13000-production-android-0000000a","versionCode":13000,"il2cppSha256":"1111111111111111111111111111111111111111111111111111111111111111","metadataSha256":"2222222222222222222222222222222222222222222222222222222222222222"}
        """;

    private static string Adaptations(params (string Source, string Game)[] adaptations) =>
        "{"
        + string.Join(",", adaptations.Select(a => $"\"{a.Source}\":{{\"game\":{a.Game}}}"))
        + "}";

    private static string AndroidRecord(string? app, string? adaptations, string? game = null) =>
        "{\"version\":\"3.4.5\",\"downloads\":{\"android-aarch64\":{"
        + string.Join(
            ",",
            new[] { ("app", app), ("adaptations", adaptations), ("game", game) }
                .Where(field => field.Item2 != null)
                .Select(field => $"\"{field.Item1}\":{field.Item2}")
        )
        + "}}}";

    private static async Task<AndroidAppRelease?> ReadAndroidApp(string body)
    {
        var result = await Fetch(
            _ => Response(HttpStatusCode.OK, body),
            platformKey: ReleaseManifestEndpoints.AndroidPlatformKey
        );
        True(result.Succeeded, "The version is still published.");
        return result.AndroidApp;
    }

    [Fact]
    public async Task Android_game_is_adapted_when_its_code_is_any_adapted_games()
    {
        var body = TestInputs.Scratch(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "latest", "android-aarch64.json")
        );
        var games = (await ReadAndroidApp(body))!.Games;
        var publisher = games.Single(game => game.Source == "publisher");
        var play = games.Single(game => game.Source == "play");
        True(
            publisher.VersionCode < play.VersionCode,
            "In the shared golden the Play game is the newer build, which the publisher case needs."
        );

        var onPlay = AndroidGameUpdate.Compare(Code(play), games);
        Equal(AndroidGameAdaptation.Adapted, onPlay.Adaptation, "The Play game is adapted.");
        True(onPlay.Game == play, "The Play adaptation decides it.");
        var onPublisher = AndroidGameUpdate.Compare(Code(publisher), games);
        Equal(
            AndroidGameAdaptation.Adapted,
            onPublisher.Adaptation,
            "The publisher game is adapted, though the Play game has a larger versionCode."
        );
        True(onPublisher.Game == publisher, "The publisher adaptation decides it.");
        Equal(
            AndroidGameAdaptation.Adapted,
            AndroidGameUpdate
                .Compare(Code(play) with { VersionCode = play.VersionCode + 50 }, games)
                .Adaptation,
            "A content-only update keeps the code, so the game stays adapted."
        );

        var other = new AndroidGameCode(0, new string('c', 64), new string('d', 64));
        var older = AndroidGameUpdate.Compare(
            other with
            {
                VersionCode = publisher.VersionCode + 1,
            },
            games
        );
        Equal(
            AndroidGameAdaptation.GameOlder,
            older.Adaptation,
            "Other code with a smaller versionCode than some adapted game is an older game."
        );
        True(older.Game == play, "The adapted game with the larger versionCode is the update.");
        Equal(
            play,
            AndroidGameUpdate
                .Compare(other with { VersionCode = publisher.VersionCode - 1 }, games)
                .Game,
            "Older than every adapted game, the newest one is the update."
        );
        var newer = AndroidGameUpdate.Compare(
            other with
            {
                VersionCode = play.VersionCode + 1,
            },
            games
        );
        Equal(
            AndroidGameAdaptation.NotAdapted,
            newer.Adaptation,
            "Other code newer than every adapted game is not adapted."
        );
        True(newer.Game == null, "No adapted game decides a game that is not adapted.");
        Equal(
            AndroidGameAdaptation.NotAdapted,
            AndroidGameUpdate
                .Compare(other with { VersionCode = play.VersionCode }, games)
                .Adaptation,
            "Other code with the newest adapted game's versionCode is not adapted."
        );

        Equal(
            AndroidGameAdaptation.Unknown,
            AndroidGameUpdate.Compare(null, games).Adaptation,
            "No Launch Record, no comparison."
        );
        Equal(
            AndroidGameAdaptation.Unknown,
            AndroidGameUpdate.Compare(Code(play), null).Adaptation,
            "No game in the release, no comparison."
        );
        Equal(
            AndroidGameAdaptation.Unknown,
            AndroidGameUpdate.Compare(Code(play), []).Adaptation,
            "A release that adapts no game, no comparison."
        );
    }

    private static AndroidGameCode Code(AndroidAdaptedGame game) =>
        new(game.VersionCode, game.Il2CppSha256, game.MetadataSha256);

    [Fact]
    public void Android_update_is_the_game_for_an_older_game_or_the_app_for_a_changed_runtime()
    {
        var newest = new AndroidAdaptedGame(
            "play",
            "1.0.6",
            13168,
            new string('a', 64),
            new string('b', 64)
        );
        var running = new AndroidAdaptedGame(
            "publisher",
            "1.0.13113-production-android-0000000b",
            13113,
            new string('c', 64),
            new string('d', 64)
        );
        var older = new AndroidGameComparison(AndroidGameAdaptation.GameOlder, newest);
        var adapted = new AndroidGameComparison(AndroidGameAdaptation.Adapted, running);
        var app = new AndroidAppDownload(new Uri("https://cdn.example/app.apk"), 65_000_000);

        var gameUpdate = AndroidGameUpdate.Needed(older, app, manualUpdate: false);
        Equal(
            AndroidUpdateReason.GameUpdated,
            gameUpdate?.Reason,
            "An older game needs a game update even when the Managed Payload could come."
        );
        Equal(
            "1.0.6",
            gameUpdate?.PublishedGameVersion,
            "The game update names the adapted game that decided it."
        );
        True(gameUpdate?.App == null, "A game update offers no BPP download.");
        Equal(
            AndroidUpdateReason.GameUpdated,
            AndroidGameUpdate.Needed(older, app, manualUpdate: true)?.Reason,
            "The game comes before a new App."
        );
        var appUpdate = AndroidGameUpdate.Needed(adapted, app, manualUpdate: true);
        Equal(
            AndroidUpdateReason.AppUpdated,
            appUpdate?.Reason,
            "The adapted game with a release this copy cannot take needs the new App."
        );
        True(appUpdate?.App == app, "The new App is offered with its download.");
        Equal(
            running.GameVersion,
            appUpdate?.PublishedGameVersion,
            "The App update names the adapted game that runs."
        );
        True(
            AndroidGameUpdate.Needed(adapted, app, manualUpdate: false) == null,
            "A release the Managed Payload brings needs nothing more."
        );
        True(
            AndroidGameUpdate.Needed(adapted, null, manualUpdate: true) == null,
            "No published App, nothing to offer."
        );
        True(
            AndroidGameUpdate.Needed(
                new AndroidGameComparison(AndroidGameAdaptation.NotAdapted, null),
                app,
                manualUpdate: true
            ) == null,
            "A release for other game code asks for nothing."
        );
        True(
            AndroidGameUpdate.Needed(
                new AndroidGameComparison(AndroidGameAdaptation.Unknown, null),
                app,
                manualUpdate: true
            ) == null,
            "An unknown game asks for nothing."
        );
    }

    [Fact]
    public void Endpoint_follows_the_platform()
    {
        Equal(
            "https://cdn.bazaarplusplus.com/installer/latest/mod/windows-x86_64.json",
            ReleaseManifestEndpoints
                .ForPlatformKey(ReleaseManifestEndpoints.WindowsPlatformKey)
                .ToString(),
            "Windows should read its own Platform Release Manifest."
        );
        Equal(
            "https://cdn.bazaarplusplus.com/installer/latest/mod/darwin-aarch64.json",
            ReleaseManifestEndpoints
                .ForPlatformKey(ReleaseManifestEndpoints.MacPlatformKey)
                .ToString(),
            "macOS should read its own Platform Release Manifest."
        );
        Equal(
            "https://cdn.bazaarplusplus.com/installer/latest/mod/android-aarch64.json",
            ReleaseManifestEndpoints
                .ForPlatformKey(ReleaseManifestEndpoints.AndroidPlatformKey)
                .ToString(),
            "Android should read its own Platform Release Manifest."
        );
        Equal(
            "https://cdn.bazaarplusplus.com/installer/latest.json",
            ReleaseManifestEndpoints.ForPlatformKey(null).ToString(),
            "An unknown platform should fall back to the lockstep Release Manifest."
        );
        Equal(
            "https://cdn.bazaarplusplus.com/installer/latest.json",
            ReleaseManifestEndpoints.ForPlatformKey("linux-x86_64").ToString(),
            "An undeclared platform key should fall back to the lockstep Release Manifest."
        );
    }

    [Fact]
    public async Task Incomplete_managed_payload_is_absent()
    {
        const string body = """
            {"version":"3.4.5","downloads":{"darwin-aarch64":{"managedPayload":{"url":"https://cdn.example/a.zip","size":3,"sha256":"aa","signature":"s"},"historyDatabaseUserVersion":1}}}
            """;
        var result = await Fetch(
            _ => Response(HttpStatusCode.OK, body),
            platformKey: ReleaseManifestEndpoints.MacPlatformKey
        );

        True(result.Succeeded, "The version is still published.");
        True(
            result.ManagedPayload == null,
            "A record without its non-managed payload digest is no Managed Payload."
        );
        var unasked = await Fetch(_ => Response(HttpStatusCode.OK, body));
        True(unasked.ManagedPayload == null, "Without a platform key only the version is read.");
    }

    [Fact]
    public async Task Successful_manifest_is_trimmed()
    {
        var result = await Fetch(_ => Response(HttpStatusCode.OK, """{"version":" 3.4.5 "}"""));

        True(result.Succeeded, "A valid latest manifest should succeed.");
        Equal("3.4.5", result.Version, "The adapter should normalize surrounding whitespace.");
    }

    [Fact]
    public async Task Unchanged_manifest_is_answered_from_the_last_read()
    {
        var body = TestInputs.Scratch(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "latest", "android-aarch64.json")
        );
        const string newer = """{"version":"9.9.9"}""";
        var sentValidators = new List<string?>();
        var respond = new Queue<Func<HttpResponseMessage>>(
            new Func<HttpResponseMessage>[]
            {
                () => Tagged(Response(HttpStatusCode.OK, body), "\"v1\""),
                () => new HttpResponseMessage(HttpStatusCode.NotModified),
                () => Tagged(Response(HttpStatusCode.OK, newer), "\"v2\""),
                () => Response(HttpStatusCode.OK, newer),
                () => new HttpResponseMessage(HttpStatusCode.NotModified),
            }
        );
        using var http = new HttpClient(
            new Handler(request =>
            {
                sentValidators.Add(request.Headers.IfNoneMatch.SingleOrDefault()?.Tag);
                return respond.Dequeue()();
            })
        );
        var client = new ReleaseManifestClient(
            http,
            new Uri("https://example.test/latest/android-aarch64.json"),
            ReleaseManifestEndpoints.AndroidPlatformKey
        );

        var first = await client.FetchAsync(default);
        var unchanged = await client.FetchAsync(default);
        var changed = await client.FetchAsync(default);
        var untagged = await client.FetchAsync(default);
        var unaskedNotModified = await client.FetchAsync(default);

        Equal(
            "[none] \"v1\" \"v1\" \"v2\" [none]",
            string.Join(" ", sentValidators.Select(tag => tag ?? "[none]")),
            "Every check asks the server, conditionally on the last tagged answer only."
        );
        True(unchanged.Succeeded, "A 304 for the remembered answer is a successful check.");
        Equal(first.Version, unchanged.Version, "A 304 answers with the remembered version.");
        True(
            unchanged.ManagedPayload == first.ManagedPayload
                && unchanged.AndroidApp == first.AndroidApp,
            "A 304 answers with the remembered Managed Payload and APK."
        );
        Equal("9.9.9", changed.Version, "A changed manifest replaces the remembered answer.");
        Equal("9.9.9", untagged.Version, "An untagged answer is still read.");
        Equal(
            ReleaseManifestFailureKind.HttpFailureStatus,
            unaskedNotModified.FailureKind,
            "A 304 to an unconditional request has nothing to answer with."
        );
        Equal(304, unaskedNotModified.HttpStatus, "The stray 304 stays diagnostic.");
    }

    private static HttpResponseMessage Tagged(HttpResponseMessage response, string etag)
    {
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    [Fact]
    public async Task Http_status_is_a_closed_failure()
    {
        var result = await Fetch(_ => Response(HttpStatusCode.ServiceUnavailable, "down"));

        Equal(
            ReleaseManifestFailureKind.HttpFailureStatus,
            result.FailureKind,
            "A non-success response should have a closed failure kind."
        );
        Equal(503, result.HttpStatus, "The response status should remain diagnostic metadata.");
    }

    [Fact]
    public async Task Missing_and_malformed_manifests_are_closed_failures()
    {
        var missing = await Fetch(_ => Response(HttpStatusCode.OK, "{}"));
        var malformed = await Fetch(_ => Response(HttpStatusCode.OK, "{"));

        Equal(
            ReleaseManifestFailureKind.ManifestVersionMissing,
            missing.FailureKind,
            "A manifest without version should fail explicitly."
        );
        Equal(
            ReleaseManifestFailureKind.RequestException,
            malformed.FailureKind,
            "Malformed JSON should remain a request-path exception outcome."
        );
        True(
            malformed.Exception != null,
            "Malformed JSON should preserve its diagnostic exception."
        );
    }

    [Fact]
    public async Task Timeout_and_caller_cancellation_remain_distinct()
    {
        var timedOut = await Fetch(_ => throw new TaskCanceledException("timeout"));
        // The request waits on the token it was sent with, so only the caller's token reaching
        // SendAsync can end it.
        using var cancellation = new CancellationTokenSource();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waiting = new HttpClient(
            new Handler(
                async (_, cancellationToken) =>
                {
                    sent.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    throw new InvalidOperationException("Unreachable.");
                }
            )
        );
        var pending = new ReleaseManifestClient(
            waiting,
            new Uri("https://example.test/latest.json")
        ).FetchAsync(cancellation.Token);
        await sent.Task;
        cancellation.Cancel();
        var cancelled = await pending;

        Equal(
            ReleaseManifestFailureKind.RequestTimedOut,
            timedOut.FailureKind,
            "An HttpClient timeout should remain observable as timeout."
        );
        Equal(
            ReleaseManifestFailureKind.Cancelled,
            cancelled.FailureKind,
            "Owner cancellation should be suppressed independently from timeout."
        );
    }

    [Fact]
    public async Task Lifecycle_rejects_out_of_order_generations_and_disposes_owners()
    {
        var lifecycle = new ReleaseManifestCheckLifecycle();
        var published = new List<string>();
        var firstOwner = new TrackingDisposable();
        var firstCompletion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var first = lifecycle.Begin(firstOwner);
        var firstRun = lifecycle.RunAsync(first, _ => firstCompletion.Task, published.Add);

        var secondOwner = new TrackingDisposable();
        var secondCompletion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var second = lifecycle.Begin(secondOwner);
        var secondRun = lifecycle.RunAsync(second, _ => secondCompletion.Task, published.Add);

        secondCompletion.SetResult("new");
        await secondRun;
        firstCompletion.SetResult("old");
        await firstRun;

        Equal(1, published.Count, "Only one out-of-order completion should publish.");
        Equal("new", published[0], "The newest request result should win.");
        True(first.CancellationToken.IsCancellationRequested, "Replacing a lease must cancel it.");
        Equal(1, firstOwner.DisposeCount, "Replacing a request must dispose its owner once.");
        Equal(1, secondOwner.DisposeCount, "Completing a request must dispose its owner once.");

        var destroyOwner = new TrackingDisposable();
        var destroyCompletion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var destroyLease = lifecycle.Begin(destroyOwner);
        var destroyRun = lifecycle.RunAsync(
            destroyLease,
            _ => destroyCompletion.Task,
            published.Add
        );

        lifecycle.Dispose();
        lifecycle.Dispose();
        destroyCompletion.SetResult("destroyed");
        await destroyRun;
        True(
            destroyLease.CancellationToken.IsCancellationRequested,
            "Dispose must cancel the current lease."
        );
        Equal(1, destroyOwner.DisposeCount, "Dispose must release the active owner once.");
        Equal(1, published.Count, "A completion after dispose must not publish.");
    }

    private static async Task<ReleaseManifestFetchResult> Fetch(
        Func<HttpRequestMessage, HttpResponseMessage> send,
        CancellationToken cancellationToken = default,
        string? platformKey = null
    )
    {
        using var http = new HttpClient(new Handler(send));
        var client = new ReleaseManifestClient(
            http,
            new Uri("https://example.test/latest.json"),
            platformKey
        );
        return await client.FetchAsync(cancellationToken);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    // The capsule's checks kept their messages; xUnit reports them with the values compared.
    private static void Equal<T>(T expected, T actual, string message) =>
        Assert.True(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message} Expected={expected}; Actual={actual}"
        );

    private static void True(bool condition, string message) => Assert.True(condition, message);
}

internal sealed class Handler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

    internal Handler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : this((request, _) => Task.FromResult(send(request))) { }

    internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        _send = send;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => _send(request, cancellationToken);
}

internal sealed class TrackingDisposable : IDisposable
{
    internal int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
