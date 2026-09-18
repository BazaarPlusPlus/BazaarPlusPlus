#nullable enable
using System.Net;
using BazaarPlusPlus.ModApi.Clients;
using Newtonsoft.Json.Linq;

internal static class BazaarDbLinkClientTests
{
    public static void Run()
    {
        LinkedResponsePostsCasePreservedCodeAndSnakeCaseAccountId();
        NonOkSuccessResponsePreservesStatusCode();
        InvalidOrExpiredErrorClassifiesAsInvalidOrExpired();
        AlreadyLinkedErrorClassifiesAsAlreadyLinked();
        ConflictStatusWithoutTokenClassifiesAsAlreadyLinked();
        MissingFieldTokenClassifiesAsMissingFields();
        MissingFieldServerSentenceClassifiesAsMissingFields();
        ServerErrorClassifiesAsServerError();
        EmptyCodeReturnsMissingFieldsWithoutHttpCall();
        CancellationRethrows();
        TransportExceptionMapsToTransportOutcome();
        DefaultRedeemEndpointPostsToFixedBazaarDbUri();
        LinkClientDisposesOwnedHttpClient();
        Console.WriteLine("BazaarDbLinkClientTests passed.");
    }

    private static void LinkedResponsePostsCasePreservedCodeAndSnakeCaseAccountId()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync(" AbcD23 ", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(result.Outcome == BazaarDbLinkOutcome.Linked, "200 response should link profile.");
        Assert(result.Succeeded, "Linked result should succeed.");
        Assert(handler.Requests.Count == 1, "Link client should send one POST.");
        var request = handler.Requests[0];
        Assert(request.Method == HttpMethod.Post, "Link redeem should use POST.");
        Assert(
            request.RequestUri?.ToString() == "https://example.invalid/api/profile/link/redeem",
            "Link redeem should post to the configured absolute endpoint."
        );
        Assert(
            handler.ContentTypes[0] == "application/json",
            "Link redeem should send application/json."
        );

        var json = JObject.Parse(handler.Bodies[0]);
        Assert((string?)json["code"] == "AbcD23", "Code should be trimmed but case-preserved.");
        Assert((string?)json["account_id"] == "acct-1", "DTO should include account_id.");
        Assert(json["accountId"] == null, "DTO should not emit camelCase accountId.");
    }

    private static void NonOkSuccessResponsePreservesStatusCode()
    {
        var result = RedeemWithResponse(HttpStatusCode.Accepted, string.Empty);
        Assert(result.Outcome == BazaarDbLinkOutcome.Linked, "202 response should link profile.");
        Assert(
            result.StatusCode == 202,
            "Linked result should preserve the actual success status."
        );
    }

    private static void InvalidOrExpiredErrorClassifiesAsInvalidOrExpired()
    {
        var result = RedeemWithResponse(
            HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_or_expired\"}"
        );
        Assert(
            result.Outcome == BazaarDbLinkOutcome.InvalidOrExpired,
            "invalid_or_expired should classify as InvalidOrExpired."
        );
    }

    private static void AlreadyLinkedErrorClassifiesAsAlreadyLinked()
    {
        var result = RedeemWithResponse(HttpStatusCode.Conflict, "{\"error\":\"already_linked\"}");
        Assert(
            result.Outcome == BazaarDbLinkOutcome.AlreadyLinked,
            "already_linked should classify as AlreadyLinked."
        );
    }

    private static void ConflictStatusWithoutTokenClassifiesAsAlreadyLinked()
    {
        // Locks the status==409 shortcut so a missing/garbled error body still maps to the contract's
        // permanent "already linked to a different user" failure rather than a retryable outcome.
        var result = RedeemWithResponse(HttpStatusCode.Conflict, string.Empty);
        Assert(
            result.Outcome == BazaarDbLinkOutcome.AlreadyLinked,
            "409 without an error token should still classify as AlreadyLinked."
        );
    }

    private static void MissingFieldTokenClassifiesAsMissingFields()
    {
        var result = RedeemWithResponse(HttpStatusCode.BadRequest, "{\"error\":\"missing_field\"}");
        Assert(
            result.Outcome == BazaarDbLinkOutcome.MissingFields,
            "missing_field token should classify as MissingFields."
        );
    }

    private static void MissingFieldServerSentenceClassifiesAsMissingFields()
    {
        // The documented server token is the human sentence "Missing code or account_id", matched by
        // prefix in the client. Guards against the StartsWith branch being dropped.
        var result = RedeemWithResponse(
            HttpStatusCode.BadRequest,
            "{\"error\":\"Missing code or account_id\"}"
        );
        Assert(
            result.Outcome == BazaarDbLinkOutcome.MissingFields,
            "Documented 'Missing code or account_id' sentence should classify as MissingFields."
        );
    }

    private static void ServerErrorClassifiesAsServerError()
    {
        var result = RedeemWithResponse(
            HttpStatusCode.InternalServerError,
            "{\"error\":\"Redeem failed\"}"
        );
        Assert(
            result.Outcome == BazaarDbLinkOutcome.ServerError,
            "500 should classify as ServerError."
        );
    }

    private static void EmptyCodeReturnsMissingFieldsWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync("  ", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(
            result.Outcome == BazaarDbLinkOutcome.MissingFields,
            "Empty code should classify as MissingFields."
        );
        Assert(handler.Requests.Count == 0, "Empty code should not send an HTTP request.");
    }

    private static void CancellationRethrows()
    {
        var handler = new RecordingHandler(_ => throw new OperationCanceledException("cancelled"));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        try
        {
            client.RedeemAsync("AbcD23", "acct-1", CancellationToken.None).GetAwaiter().GetResult();
            throw new InvalidOperationException("Cancellation should be rethrown.");
        }
        catch (OperationCanceledException)
        {
            Assert(
                handler.Requests.Count == 1,
                "Cancellation should occur after one HTTP attempt."
            );
        }
    }

    private static void TransportExceptionMapsToTransportOutcome()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("DNS exploded"));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync("AbcD23", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(
            result.Outcome == BazaarDbLinkOutcome.Transport,
            "Transport exception should classify as Transport."
        );
        Assert(result.StatusCode == null, "Transport exception should not have an HTTP status.");
        Assert(
            result.Error == "transport_error",
            "Transport exception should expose only the closed user code."
        );
        Assert(
            result.DiagnosticException?.Message == "DNS exploded",
            "Transport exception should remain available to diagnostics."
        );
    }

    private static void DefaultRedeemEndpointPostsToFixedBazaarDbUri()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint)
        );

        client.RedeemAsync("AbcD23", "acct-1", CancellationToken.None).GetAwaiter().GetResult();

        Assert(handler.Requests.Count == 1, "Default endpoint test should send one POST.");
        Assert(
            handler.Requests[0].RequestUri?.ToString()
                == "https://bazaardb.gg/api/profile/link/redeem",
            "Default endpoint should post to the fixed BazaarDB redeem URI."
        );
    }

    private static BazaarDbLinkResult RedeemWithResponse(HttpStatusCode statusCode, string body)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body),
        });
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        var result = client
            .RedeemAsync("AbcD23", "acct-1", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(handler.Requests.Count == 1, "Link client should send one POST.");
        return result;
    }

    private static void LinkClientDisposesOwnedHttpClient()
    {
        var handler = new DisposeTrackingHandler();
        var client = new BazaarDbLinkClient(
            new HttpClient(handler),
            new Uri("https://example.invalid/api/profile/link/redeem")
        );

        Assert(
            client is IDisposable,
            "Link client should implement IDisposable because it owns its HttpClient."
        );

        ((IDisposable)client).Dispose();

        Assert(handler.Disposed, "Disposing the link client should dispose its HttpClient.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        public List<string?> ContentTypes { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            Bodies.Add(
                request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult()
                    ?? string.Empty
            );
            ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class DisposeTrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
