#nullable enable
using System.Text;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Http;

public enum ModApiEnvelopeShape
{
    None,
    NestedV5,
    LegacyTopLevel,
}

public readonly struct ModApiErrorEnvelope
{
    public ModApiErrorEnvelope(
        ModApiEnvelopeShape shape,
        string? code,
        string? message,
        bool? retryable,
        string? requestId
    )
    {
        Shape = shape;
        Code = code;
        Message = message;
        Retryable = retryable;
        RequestId = requestId;
    }

    public ModApiEnvelopeShape Shape { get; }
    public string? Code { get; }
    public string? Message { get; }
    public bool? Retryable { get; }
    public string? RequestId { get; }
}

public readonly struct ModApiBodyReadPolicy
{
    public ModApiBodyReadPolicy(int maxBytes, string overflowUserCode)
    {
        MaxBytes = maxBytes;
        OverflowUserCode = overflowUserCode;
    }

    public int MaxBytes { get; }
    public string OverflowUserCode { get; }

    public static ModApiBodyReadPolicy Json { get; } = new(1024 * 1024, "response_too_large");
}

public sealed class ModApiResponse
{
    private ModApiResponse(
        int statusCode,
        bool isSuccess,
        string body,
        ModApiErrorEnvelope error,
        string? requestId,
        int? retryAfterSeconds,
        string userCode
    )
    {
        StatusCode = statusCode;
        IsSuccess = isSuccess;
        Body = body;
        Error = error;
        RequestId = requestId;
        RetryAfterSeconds = retryAfterSeconds;
        UserCode = userCode;
    }

    public int StatusCode { get; }
    public bool IsSuccess { get; }
    public string Body { get; }
    public ModApiErrorEnvelope Error { get; }
    public string? RequestId { get; }
    public int? RetryAfterSeconds { get; }
    public string UserCode { get; }

    public static async Task<ModApiResponse> ReadAsync(
        HttpResponseMessage response,
        ModApiBodyReadPolicy bodyPolicy,
        CancellationToken cancellationToken = default
    )
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        if (bodyPolicy.MaxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(bodyPolicy));
        if (string.IsNullOrWhiteSpace(bodyPolicy.OverflowUserCode))
            throw new ArgumentException("Overflow user code is required.", nameof(bodyPolicy));

        var statusCode = (int)response.StatusCode;
        var retryAfterSeconds = ReadRetryAfterSeconds(response);
        var headerRequestId = response.Headers.TryGetValues("X-Request-Id", out var requestIds)
            ? Normalize(requestIds.FirstOrDefault())
            : null;

        if (
            response.Content.Headers.ContentLength is { } declaredLength
            && declaredLength > bodyPolicy.MaxBytes
        )
        {
            return new ModApiResponse(
                statusCode,
                false,
                string.Empty,
                default,
                headerRequestId,
                retryAfterSeconds,
                bodyPolicy.OverflowUserCode
            );
        }

        var body = await ReadBodyAsync(response.Content, bodyPolicy.MaxBytes, cancellationToken)
            .ConfigureAwait(false);
        if (body == null)
        {
            return new ModApiResponse(
                statusCode,
                false,
                string.Empty,
                default,
                headerRequestId,
                retryAfterSeconds,
                bodyPolicy.OverflowUserCode
            );
        }

        var error = ParseError(body);
        var requestId = headerRequestId ?? error.RequestId;
        var isSuccess = response.IsSuccessStatusCode;
        // Remote codes remain protocol metadata. Endpoint clients may explicitly allow-list a
        // code for product behavior, but arbitrary server text must never become user-facing.
        var userCode = isSuccess ? "ok" : $"http_{statusCode}";
        return new ModApiResponse(
            statusCode,
            isSuccess,
            body,
            error,
            requestId,
            retryAfterSeconds,
            userCode
        );
    }

    private static async Task<string?> ReadBodyAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken
    )
    {
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(81920, maxBytes + 1)];
        while (true)
        {
            var read = await stream
                .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                return Encoding.UTF8.GetString(output.ToArray());
            if (output.Length + read > maxBytes)
                return null;
            output.Write(buffer, 0, read);
        }
    }

    private static ModApiErrorEnvelope ParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return default;
        try
        {
            var root = JObject.Parse(body);
            if (root["error"] is JObject nested)
            {
                return new ModApiErrorEnvelope(
                    ModApiEnvelopeShape.NestedV5,
                    ReadString(nested, "code"),
                    ReadMessage(nested),
                    nested["retryable"]?.Value<bool?>(),
                    ReadString(nested, "request_id")
                );
            }

            var legacyCode = ReadString(root, "error") ?? ReadString(root, "code");
            if (legacyCode != null)
            {
                return new ModApiErrorEnvelope(
                    ModApiEnvelopeShape.LegacyTopLevel,
                    legacyCode,
                    ReadMessage(root),
                    root["retryable"]?.Value<bool?>(),
                    ReadString(root, "request_id")
                );
            }
        }
        catch
        {
            // Malformed and non-JSON bodies deliberately collapse to the status-derived user code.
        }
        return default;
    }

    private static string? ReadMessage(JObject source) =>
        ReadString(source, "message")
        ?? ReadString(source, "reason")
        ?? ReadString(source, "detail");

    private static string? ReadString(JObject source, string name) =>
        source[name]?.Type == JTokenType.String ? Normalize(source[name]?.Value<string>()) : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ReadRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return Math.Max(0, (int)Math.Ceiling(delta.TotalSeconds));
        if (retryAfter?.Date is { } date)
            return Math.Max(0, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
        return null;
    }
}

public sealed class ModApiFailure
{
    public ModApiFailure(
        string userCode,
        Exception? diagnosticException = null,
        ModApiResponse? response = null
    )
    {
        UserCode = string.IsNullOrWhiteSpace(userCode)
            ? throw new ArgumentException("User code is required.", nameof(userCode))
            : userCode;
        DiagnosticException = diagnosticException;
        Response = response;
    }

    public string UserCode { get; }
    public Exception? DiagnosticException { get; }
    public ModApiResponse? Response { get; }
}
