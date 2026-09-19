using System.Net;
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure.ReleaseManifest;

await TestSuccessfulManifestIsTrimmed();
await TestHttpStatusIsClosedFailure();
await TestMissingAndMalformedManifestsAreClosedFailures();
await TestTimeoutAndCallerCancellationRemainDistinct();
await TestLifecycleRejectsOutOfOrderGenerationsAndDisposesOwners();

Console.WriteLine("Release manifest client checks passed.");

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
    CancellationToken cancellationToken = default
)
{
    using var http = new HttpClient(new Handler(send));
    var client = new ReleaseManifestClient(http, new Uri("https://example.test/latest.json"));
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
