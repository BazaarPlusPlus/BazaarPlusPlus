#nullable enable
using System.Net;
using BazaarPlusPlus.ModApi.Clients;

internal static class HealthClientTests
{
    public static async Task RunAsync()
    {
        var success = await ProbeAsync(_ =>
            Json("{\"status\":\"ok\",\"server_time_ms\":1780444800000}")
        );
        Assert(success.Succeeded, "Successful health response should be available.");
        Assert(success.RoundTripMilliseconds >= 0, "Successful health probe should expose RTT.");

        var badStatus = await ProbeAsync(_ =>
            Json("{\"status\":\"degraded\",\"server_time_ms\":1780444800000}")
        );
        Assert(!badStatus.Succeeded, "Non-ok health status should fail availability.");
        Assert(badStatus.Error == "health_status_not_ok", "Bad status should have a stable error.");

        var missingTimestamp = await ProbeAsync(_ => Json("{\"status\":\"ok\"}"));
        Assert(!missingTimestamp.Succeeded, "Missing server timestamp should fail availability.");
        Assert(
            missingTimestamp.Error == "server_time_invalid",
            "Missing timestamp should have the same stable error as an invalid timestamp."
        );

        var legacyShape = await ProbeAsync(_ => Json("{\"ok\":true}"));
        Assert(
            !legacyShape.Succeeded,
            "Legacy { ok: true } health shape should not satisfy the new timestamp/status contract."
        );

        var badTimestamp = await ProbeAsync(_ =>
            Json("{\"status\":\"ok\",\"server_time_ms\":\"bad\"}")
        );
        Assert(!badTimestamp.Succeeded, "Invalid server timestamp should fail availability.");
        Assert(
            badTimestamp.Error == "server_time_invalid",
            "Bad timestamp should have a stable error."
        );

        var httpFailure = await ProbeAsync(_ => new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable
        ));
        Assert(!httpFailure.Succeeded, "HTTP failure should fail availability.");
        Assert(httpFailure.Error == "http_503", "HTTP failure should include status code.");

        var exceptionFailure = await ProbeAsync(_ =>
            throw new HttpRequestException("network down")
        );
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

    // Each case probes through the public session, so the /health route and the
    // response contract are exercised exactly as the History panel reaches them.
    private static async Task<ModApiHealthProbeResult> ProbeAsync(
        Func<HttpRequestMessage, HttpResponseMessage> responder
    )
    {
        using var session =
            ModApiSession.TryCreate(
                "https://example.invalid",
                "1.0.0",
                "HealthClientTests",
                TimeSpan.FromSeconds(30),
                new StubHandler(responder)
            ) ?? throw new InvalidOperationException("valid session");
        return await session.ProbeHealthAsync(CancellationToken.None);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(responder(request));
    }
}
