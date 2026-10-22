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
await TestIncompleteManagedPayloadIsAbsent();
await TestSuccessfulManifestIsTrimmed();
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
        var result = await Fetch(_ => Response(HttpStatusCode.OK, body), platformKey: platformKey);

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
        release.GetProperty("game").GetProperty("versionCode").GetInt32(),
        result.AndroidApk!.GameVersionCode,
        "The APK is identified by the game version code it was injected into."
    );
    Equal(
        release.GetProperty("apk").GetProperty("url").GetString(),
        result.AndroidApk.Url.ToString(),
        "The APK URL is the writer's."
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
    True(incomplete.AndroidApk == null, "An APK without its game version code is no APK.");

    const string plainHttp = """
        {"version":"3.4.5","downloads":{"android-aarch64":{"apk":{"url":"http://cdn.example/a.apk"},"game":{"versionCode":13000}}}}
        """;
    var insecure = await Fetch(
        _ => Response(HttpStatusCode.OK, plainHttp),
        platformKey: ReleaseManifestEndpoints.AndroidPlatformKey
    );
    True(insecure.AndroidApk == null, "An APK link that is not HTTPS is never offered.");
}

static void TestAndroidGameUpdateComparesVersionCodes()
{
    var apk = new AndroidApkRelease(12900, new Uri("https://cdn.example/a.apk"));
    Equal(
        12825,
        AndroidGameUpdate.ParseVersionCode("1.0.12825-production-android-d9601ded"),
        "The version code is the third number of the game version."
    );
    Equal<int?>(null, AndroidGameUpdate.ParseVersionCode("unknown"), "No code in junk.");
    Equal<int?>(null, AndroidGameUpdate.ParseVersionCode(""), "No code in an empty version.");
    True(
        AndroidGameUpdate.Newer("1.0.12825-production-android-d9601ded", apk) == apk,
        "A newer published game is offered."
    );
    True(
        AndroidGameUpdate.Newer("1.0.12900-production-android-0000000a", apk) == null,
        "The same game is current."
    );
    True(
        AndroidGameUpdate.Newer("1.0.13000-production-android-0000000a", apk) == null,
        "An older published game is never offered."
    );
    True(
        AndroidGameUpdate.Newer("garbled", apk) == null,
        "An unreadable running version offers nothing."
    );
    True(
        AndroidGameUpdate.Newer("1.0.12825-production-android-d9601ded", null) == null,
        "No APK, no offer."
    );
}

static void TestEndpointFollowsThePlatform()
{
    Equal(
        "https://cdn.bazaarplusplus.com/installer/latest/windows-x86_64.json",
        ReleaseManifestEndpoints
            .ForPlatformKey(ReleaseManifestEndpoints.WindowsPlatformKey)
            .ToString(),
        "Windows should read its own Platform Release Manifest."
    );
    Equal(
        "https://cdn.bazaarplusplus.com/installer/latest/darwin-aarch64.json",
        ReleaseManifestEndpoints.ForPlatformKey(ReleaseManifestEndpoints.MacPlatformKey).ToString(),
        "macOS should read its own Platform Release Manifest."
    );
    Equal(
        "https://cdn.bazaarplusplus.com/installer/latest/android-aarch64.json",
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
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var cancelled = await Fetch(
        _ => throw new OperationCanceledException(cancellation.Token),
        cancellation.Token
    );

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
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

    internal Handler(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        _send = send;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => Task.FromResult(_send(request));
}

internal sealed class TrackingDisposable : IDisposable
{
    internal int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
