#nullable enable
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BazaarPlusPlus.ModApi.Http;

internal static class ModApiResponseTests
{
    public static async Task RunAsync()
    {
        await ReadsNestedAndLegacyEnvelopes();
        await ResolvesRequestIdAndRetryAfter();
        await CollapsesMalformedBlankAndOversizedBodies();
        TransportFailureSeparatesUserAndDiagnosticChannels();
        Console.WriteLine("ModApiResponseTests passed.");
    }

    private static async Task ReadsNestedAndLegacyEnvelopes()
    {
        using var nestedMessage = Json(
            HttpStatusCode.Conflict,
            "{\"error\":{\"code\":\"bundle_id_conflict\",\"message\":\"private\",\"retryable\":false,\"request_id\":\"body-nested\"}}"
        );
        var nested = await ModApiResponse.ReadAsync(nestedMessage, ModApiBodyReadPolicy.Json);
        Assert(nested.Error.Shape == ModApiEnvelopeShape.NestedV5, "nested V5 shape");
        Assert(nested.Error.Code == "bundle_id_conflict", "nested protocol code");
        Assert(nested.UserCode == "http_409", "nested response uses closed user code");
        Assert(nested.RequestId == "body-nested", "nested request ID fallback");
        Assert(nested.Error.Retryable == false, "nested retryable flag");

        using var legacyMessage = Json(
            (HttpStatusCode)429,
            "{\"error\":\"rate_limited\",\"detail\":\"private\",\"retryable\":true,\"request_id\":\"body-legacy\"}"
        );
        var legacy = await ModApiResponse.ReadAsync(legacyMessage, ModApiBodyReadPolicy.Json);
        Assert(legacy.Error.Shape == ModApiEnvelopeShape.LegacyTopLevel, "legacy shape");
        Assert(legacy.Error.Code == "rate_limited", "legacy protocol code");
        Assert(legacy.UserCode == "http_429", "legacy response uses closed user code");
        Assert(legacy.Error.Retryable == true, "legacy retryable flag");

        using var sentinelMessage = Json(
            HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"private-remote-sentinel\"}}"
        );
        var sentinel = await ModApiResponse.ReadAsync(sentinelMessage, ModApiBodyReadPolicy.Json);
        Assert(
            sentinel.Error.Code == "private-remote-sentinel",
            "diagnostic protocol code retained"
        );
        Assert(sentinel.UserCode == "http_403", "arbitrary remote code is not user-facing");
    }

    private static async Task ResolvesRequestIdAndRetryAfter()
    {
        using var deltaMessage = Json(
            (HttpStatusCode)429,
            "{\"error\":{\"code\":\"rate_limited\",\"request_id\":\"body-id\"}}"
        );
        deltaMessage.Headers.TryAddWithoutValidation("X-Request-Id", "header-id");
        deltaMessage.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
        var delta = await ModApiResponse.ReadAsync(deltaMessage, ModApiBodyReadPolicy.Json);
        Assert(delta.RequestId == "header-id", "header request ID wins");
        Assert(delta.RetryAfterSeconds == 17, "Retry-After delta");

        using var dateMessage = Json(HttpStatusCode.ServiceUnavailable, "{}");
        dateMessage.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddSeconds(30)
        );
        var date = await ModApiResponse.ReadAsync(dateMessage, ModApiBodyReadPolicy.Json);
        Assert(date.RetryAfterSeconds is >= 28 and <= 30, "Retry-After date");
    }

    private static async Task CollapsesMalformedBlankAndOversizedBodies()
    {
        using var malformedMessage = Json(HttpStatusCode.BadGateway, "private-sentinel{");
        var malformed = await ModApiResponse.ReadAsync(malformedMessage, ModApiBodyReadPolicy.Json);
        Assert(malformed.Error.Shape == ModApiEnvelopeShape.None, "malformed has no shape");
        Assert(malformed.UserCode == "http_502", "malformed uses closed status code");

        using var blankMessage = Json(HttpStatusCode.BadRequest, string.Empty);
        var blank = await ModApiResponse.ReadAsync(blankMessage, ModApiBodyReadPolicy.Json);
        Assert(blank.UserCode == "http_400", "blank uses closed status code");

        using var oversizedMessage = Json(HttpStatusCode.BadRequest, new string('x', 33));
        var oversized = await ModApiResponse.ReadAsync(
            oversizedMessage,
            new ModApiBodyReadPolicy(32, "body_too_large")
        );
        Assert(!oversized.IsSuccess, "overflow cannot be success");
        Assert(oversized.Body.Length == 0, "overflow body is not retained");
        Assert(oversized.UserCode == "body_too_large", "overflow closed code");
    }

    private static void TransportFailureSeparatesUserAndDiagnosticChannels()
    {
        var exception = new InvalidOperationException("private-transport-sentinel");
        var failure = new ModApiFailure("transport_error", exception);
        Assert(failure.UserCode == "transport_error", "closed transport code");
        Assert(
            ReferenceEquals(failure.DiagnosticException, exception),
            "diagnostic exception retained"
        );
        Assert(!failure.UserCode.Contains("sentinel", StringComparison.Ordinal), "no raw UI text");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
