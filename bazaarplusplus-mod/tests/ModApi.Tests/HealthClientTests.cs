#nullable enable
using System.Net;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;

internal static class HealthClientTests
{
    public static void Run()
    {
        var routes =
            ModApiRoutes.TryCreate("https://example.invalid")
            ?? throw new InvalidOperationException("TryCreate returned null for valid URL");

        var successHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\",\"server_time_ms\":1780444800000}"),
        });
        var successClient = new ModApiHealthClient(new HttpClient(successHandler), routes);
        var success = successClient.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(success.Succeeded, "Successful health response should be available.");
        Assert(success.Status == "ok", "Successful health response should expose status.");
        Assert(
            success.ServerTimeUtc.HasValue,
            "Successful health response should expose server time."
        );
        Assert(success.RoundTripMilliseconds >= 0, "Successful health probe should expose RTT.");
        Assert(
            successHandler.Requests[0].RequestUri!.ToString() == "https://example.invalid/health",
            "Health client should call the health route."
        );

        var badStatusClient = new ModApiHealthClient(
            new HttpClient(
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"degraded\",\"server_time_ms\":1780444800000}"
                    ),
                })
            ),
            routes
        );
        var badStatus = badStatusClient.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert(!badStatus.Succeeded, "Non-ok health status should fail availability.");
        Assert(badStatus.Error == "health_status_not_ok", "Bad status should have a stable error.");

        var missingTimestampClient = new ModApiHealthClient(
            new HttpClient(
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}"),
                })
            ),
            routes
        );
        var missingTimestamp = missingTimestampClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!missingTimestamp.Succeeded, "Missing server timestamp should fail availability.");
        Assert(
            missingTimestamp.Error == "server_time_invalid",
            "Missing timestamp should have the same stable error as an invalid timestamp."
        );

        var legacyShapeClient = new ModApiHealthClient(
            new HttpClient(
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}"),
                })
            ),
            routes
        );
        var legacyShape = legacyShapeClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(
            !legacyShape.Succeeded,
            "Legacy { ok: true } health shape should not satisfy the new timestamp/status contract."
        );

        var badTimestampClient = new ModApiHealthClient(
            new HttpClient(
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"server_time_ms\":\"bad\"}"),
                })
            ),
            routes
        );
        var badTimestamp = badTimestampClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!badTimestamp.Succeeded, "Invalid server timestamp should fail availability.");
        Assert(
            badTimestamp.Error == "server_time_invalid",
            "Bad timestamp should have a stable error."
        );

        var httpFailureClient = new ModApiHealthClient(
            new HttpClient(
                new RecordingHandler(_ => new HttpResponseMessage(
                    HttpStatusCode.ServiceUnavailable
                ))
            ),
            routes
        );
        var httpFailure = httpFailureClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!httpFailure.Succeeded, "HTTP failure should fail availability.");
        Assert(httpFailure.Error == "http_503", "HTTP failure should include status code.");

        var exceptionClient = new ModApiHealthClient(
            new HttpClient(
                new RecordingHandler(_ => throw new HttpRequestException("network down"))
            ),
            routes
        );
        var exceptionFailure = exceptionClient
            .ProbeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert(!exceptionFailure.Succeeded, "Transport exceptions should fail availability.");
        Assert(
            exceptionFailure.Error == "transport_error",
            "Transport exceptions should expose only the closed user code."
        );
        Assert(
            exceptionFailure.DiagnosticException?.Message == "network down",
            "Transport exceptions should remain available to diagnostics."
        );
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
