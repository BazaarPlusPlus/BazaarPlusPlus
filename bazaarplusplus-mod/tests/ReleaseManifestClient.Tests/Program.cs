using System.Net;
using System.Text.Json;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;
using BazaarPlusPlus.TestSupport;

await TestSharedReleaseManifest();
await TestSharedPlatformReleaseManifests();
TestEndpointFollowsThePlatform();
await TestAndroidApkIsReadOnlyWhenComplete();
TestAndroidGameUpdateComparesVersionCodes();
TestAndroidUpdateIsTheGameForANewerGameOrTheAppForAChangedRuntime();
await TestIncompleteManagedPayloadIsAbsent();
await TestSuccessfulManifestIsTrimmed();
await TestUnchangedManifestIsAnsweredFromTheLastRead();
await TestHttpStatusIsClosedFailure();
await TestMissingAndMalformedManifestsAreClosedFailures();
await TestTimeoutAndCallerCancellationRemainDistinct();
await TestLifecycleRejectsOutOfOrderGenerationsAndDisposesOwners();

Console.WriteLine("Release manifest client checks passed.");

static async Task TestSharedReleaseManifest()
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

static async Task TestSharedPlatformReleaseManifests()
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
        Equal(expectedVersion, result.ManagedPayload!.Version, "The payload is that version's.");
        Equal(
            payload.GetProperty("url").GetString(),
            result.ManagedPayload.Url.ToString(),
            "The payload URL is the writer's."
        );
        Equal(payload.GetProperty("size").GetInt64(), result.ManagedPayload.Size, "Size.");
        Equal(payload.GetProperty("sha256").GetString(), result.ManagedPayload.Sha256, "SHA-256.");
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

static async Task TestAndroidApkIsReadOnlyWhenComplete()
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

    True(result.AndroidApk != null, "The Android manifest should yield its Release APK.");
    Equal(
        release.GetProperty("game").GetProperty("gameVersion").GetString(),
        result.AndroidApk!.GameVersion,
        "The APK is identified by the game version it was injected into."
    );
    Equal(
        release.GetProperty("apk").GetProperty("url").GetString(),
        result.AndroidApk.Url.ToString(),
        "The APK URL is the writer's."
    );
    Equal(
        release.GetProperty("apk").GetProperty("size").GetInt64(),
        result.AndroidApk.Size,
        "The APK size is the writer's."
    );
    Equal(
        release.GetProperty("apk").GetProperty("sha256").GetString(),
        result.AndroidApk.Sha256,
        "The APK SHA-256 is the writer's."
    );

    var desktop = await Fetch(
        _ => Response(HttpStatusCode.OK, body),
        platformKey: ReleaseManifestEndpoints.MacPlatformKey
    );
    True(desktop.AndroidApk == null, "Another platform's record names no APK.");

    const string withoutGame = """
        {"version":"3.4.5","downloads":{"android-aarch64":{"apk":{"url":"https://cdn.example/a.apk"}}}}
        """;
    var incomplete = await Fetch(
        _ => Response(HttpStatusCode.OK, withoutGame),
        platformKey: ReleaseManifestEndpoints.AndroidPlatformKey
    );
    True(incomplete.Succeeded, "The version is still published.");
    True(incomplete.AndroidApk == null, "An APK without its game version is no APK.");

    const string plainHttp = """
        {"version":"3.4.5","downloads":{"android-aarch64":{"apk":{"url":"http://cdn.example/a.apk","size":1,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"},"game":{"gameVersion":"1.0.13000-production-android-0000000a"}}}}
        """;
    var insecure = await Fetch(
        _ => Response(HttpStatusCode.OK, plainHttp),
        platformKey: ReleaseManifestEndpoints.AndroidPlatformKey
    );
    True(insecure.AndroidApk == null, "An APK link that is not HTTPS is never offered.");

    const string withoutDigest = """
        {"version":"3.4.5","downloads":{"android-aarch64":{"apk":{"url":"https://cdn.example/a.apk","size":1},"game":{"gameVersion":"1.0.13000-production-android-0000000a"}}}}
        """;
    var unverifiable = await Fetch(
        _ => Response(HttpStatusCode.OK, withoutDigest),
        platformKey: ReleaseManifestEndpoints.AndroidPlatformKey
    );
    True(unverifiable.AndroidApk == null, "An APK a download cannot check is no APK.");
}

static void TestAndroidGameUpdateComparesVersionCodes()
{
    const string published = "1.0.12900-production-android-0000000b";
    Equal(
        12825,
        AndroidGameUpdate.ParseVersionCode("1.0.12825-production-android-d9601ded"),
        "The version code is the third number of the game version."
    );
    Equal<int?>(null, AndroidGameUpdate.ParseVersionCode("unknown"), "No code in junk.");
    Equal<int?>(null, AndroidGameUpdate.ParseVersionCode(""), "No code in an empty version.");
    True(
        AndroidGameUpdate.Newer("1.0.12825-production-android-d9601ded", published),
        "A newer published game asks for a game update."
    );
    True(
        !AndroidGameUpdate.Newer("1.0.12900-production-android-0000000a", published),
        "The same game is current."
    );
    True(
        !AndroidGameUpdate.Newer("1.0.13000-production-android-0000000a", published),
        "An older published game asks for nothing."
    );
    True(
        !AndroidGameUpdate.Newer("garbled", published),
        "An unreadable running version asks for nothing."
    );
    True(
        !AndroidGameUpdate.Newer("1.0.12825-production-android-d9601ded", null),
        "No published game, no update."
    );
    True(
        !AndroidGameUpdate.Newer("1.0.12825-production-android-d9601ded", "garbled"),
        "An unreadable published version asks for nothing."
    );
}

static void TestAndroidUpdateIsTheGameForANewerGameOrTheAppForAChangedRuntime()
{
    const string game = "1.0.12825-production-android-d9601ded";
    const string newer = "1.0.12900-production-android-0000000b";
    const string older = "1.0.12800-production-android-0000000c";
    var app = new AndroidAppDownload(new Uri("https://cdn.example/app.apk"), 65_000_000);

    var gameUpdate = AndroidGameUpdate.Needed(game, newer, app, manualUpdate: false);
    Equal(
        AndroidUpdateReason.GameUpdated,
        gameUpdate?.Reason,
        "A newer game needs a game update even when the Managed Payload could come."
    );
    True(gameUpdate?.App == null, "A game update offers no BPP download.");
    Equal(
        AndroidUpdateReason.GameUpdated,
        AndroidGameUpdate.Needed(game, newer, app, manualUpdate: true)?.Reason,
        "The game comes before a new App."
    );
    var appUpdate = AndroidGameUpdate.Needed(game, game, app, manualUpdate: true);
    Equal(
        AndroidUpdateReason.AppUpdated,
        appUpdate?.Reason,
        "The same game with a release this copy cannot take needs the new App."
    );
    True(appUpdate?.App == app, "The new App is offered with its download.");
    True(
        AndroidGameUpdate.Needed(game, game, app, manualUpdate: false) == null,
        "A release the Managed Payload brings needs nothing more."
    );
    True(
        AndroidGameUpdate.Needed(game, game, null, manualUpdate: true) == null,
        "No published App, nothing to offer."
    );
    True(
        AndroidGameUpdate.Needed(game, older, app, manualUpdate: true) == null,
        "A release for an older game asks for nothing."
    );
    True(
        AndroidGameUpdate.Needed("garbled", game, app, manualUpdate: true) == null,
        "An unreadable running version asks for nothing."
    );
}

static void TestEndpointFollowsThePlatform()
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
        ReleaseManifestEndpoints.ForPlatformKey(ReleaseManifestEndpoints.MacPlatformKey).ToString(),
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

static async Task TestIncompleteManagedPayloadIsAbsent()
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

static async Task TestSuccessfulManifestIsTrimmed()
{
    var result = await Fetch(_ => Response(HttpStatusCode.OK, """{"version":" 3.4.5 "}"""));

    True(result.Succeeded, "A valid latest manifest should succeed.");
    Equal("3.4.5", result.Version, "The adapter should normalize surrounding whitespace.");
}

static async Task TestUnchangedManifestIsAnsweredFromTheLastRead()
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
            && unchanged.AndroidApk == first.AndroidApk,
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

static HttpResponseMessage Tagged(HttpResponseMessage response, string etag)
{
    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
    return response;
}

static async Task TestHttpStatusIsClosedFailure()
{
    var result = await Fetch(_ => Response(HttpStatusCode.ServiceUnavailable, "down"));

    Equal(
        ReleaseManifestFailureKind.HttpFailureStatus,
        result.FailureKind,
        "A non-success response should have a closed failure kind."
    );
    Equal(503, result.HttpStatus, "The response status should remain diagnostic metadata.");
}

static async Task TestMissingAndMalformedManifestsAreClosedFailures()
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
    True(malformed.Exception != null, "Malformed JSON should preserve its diagnostic exception.");
}

static async Task TestTimeoutAndCallerCancellationRemainDistinct()
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

static async Task TestLifecycleRejectsOutOfOrderGenerationsAndDisposesOwners()
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
    var destroyRun = lifecycle.RunAsync(destroyLease, _ => destroyCompletion.Task, published.Add);

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

static async Task<ReleaseManifestFetchResult> Fetch(
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

static HttpResponseMessage Response(HttpStatusCode status, string body) =>
    new(status) { Content = new StringContent(body) };

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
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
