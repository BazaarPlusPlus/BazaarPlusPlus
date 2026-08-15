#nullable enable
using System.Net.Http.Headers;
using BazaarPlusPlus.ModApi.Http;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Clients;

internal sealed class BundleUploadClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public BundleUploadClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BundleUploadResponse> UploadAsync(
        Stream sealedFile,
        long contentLength,
        string contentDigest,
        string expectedBundleId,
        string expectedRunId,
        CancellationToken cancellationToken
    )
    {
        if (sealedFile == null)
            throw new ArgumentNullException(nameof(sealedFile));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _routes.UploadBundle);
            request.Content = new StreamContent(sealedFile);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                Bundle.BundleLimitsV5.BundleContentType
            );
            request.Content.Headers.ContentLength = contentLength;
            request.Headers.TryAddWithoutValidation("Content-Digest", contentDigest);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var parsed = await ModApiResponse
                .ReadAsync(response, ModApiBodyReadPolicy.Json, timeout.Token)
                .ConfigureAwait(false);
            return Classify(parsed, expectedBundleId, expectedRunId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BundleUploadResponse.Transient("request_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BundleUploadResponse.Transient("transport_error", diagnosticException: ex);
        }
    }

    private static BundleUploadResponse Classify(
        ModApiResponse response,
        string expectedBundleId,
        string expectedRunId
    )
    {
        var statusCode = response.StatusCode;
        if (response.IsSuccess && statusCode is 200 or 201)
        {
            try
            {
                var receipt = JObject.Parse(response.Body);
                var bundleId = receipt["bundle_id"]?.Value<string>();
                var runId = receipt["run_id"]?.Value<string>();
                var outcome = receipt["outcome"]?.Value<string>();
                var delivery = receipt["bazaardb_delivery"]?.Value<string>();
                var validOutcome = statusCode == 201 ? outcome == "stored" : outcome == "duplicate";
                var validDelivery = delivery is "created" or "existing" or "not_applicable";
                if (
                    bundleId != expectedBundleId
                    || runId != expectedRunId
                    || !validOutcome
                    || !validDelivery
                )
                    return BundleUploadResponse.Transient(
                        "invalid_success_receipt",
                        requestId: response.RequestId
                    );
                return BundleUploadResponse.Uploaded(outcome!, response.RequestId);
            }
            catch
            {
                return BundleUploadResponse.Transient(
                    "invalid_success_receipt",
                    requestId: response.RequestId
                );
            }
        }

        var envelope = response.Error;
        var code = response.UserCode;
        var trustedConflict = envelope.Shape == ModApiEnvelopeShape.NestedV5;
        if (statusCode == 409 && trustedConflict && envelope.Code == "run_already_bundled")
            return BundleUploadResponse.Equivalent("run_already_bundled", response.RequestId);
        if (statusCode == 409 && trustedConflict && envelope.Code == "bundle_id_conflict")
            return BundleUploadResponse.Permanent(
                "bundle_id_conflict",
                envelope.Message,
                response.RequestId
            );
        if (statusCode == 409)
            return BundleUploadResponse.Transient(
                code,
                requestId: response.RequestId,
                retryAfterSeconds: response.RetryAfterSeconds
            );
        if (statusCode is 400 or 411 or 413 or 415 or 422)
            return BundleUploadResponse.Permanent(code, envelope.Message, response.RequestId);
        if (statusCode is 408 or 429 || statusCode >= 500 || envelope.Retryable == true)
            return BundleUploadResponse.Transient(
                code,
                envelope.Message,
                response.RequestId,
                response.RetryAfterSeconds
            );
        if (envelope.Shape != ModApiEnvelopeShape.None)
            return envelope.Retryable == false
                ? BundleUploadResponse.Permanent(code, envelope.Message, response.RequestId)
                : BundleUploadResponse.Transient(
                    code,
                    envelope.Message,
                    response.RequestId,
                    response.RetryAfterSeconds
                );
        return BundleUploadResponse.Transient(code, requestId: response.RequestId);
    }
}

public enum BundleUploadDisposition
{
    Uploaded,
    Equivalent,
    Permanent,
    Transient,
}

public sealed class BundleUploadResponse
{
    private BundleUploadResponse(
        BundleUploadDisposition disposition,
        string code,
        string? detail,
        string? requestId,
        string? outcome,
        int? retryAfterSeconds,
        Exception? diagnosticException
    )
    {
        Disposition = disposition;
        Code = code;
        Detail = detail;
        RequestId = requestId;
        Outcome = outcome;
        RetryAfterSeconds = retryAfterSeconds;
        DiagnosticException = diagnosticException;
    }

    public BundleUploadDisposition Disposition { get; }
    public string Code { get; }
    public string? Detail { get; }
    public string? RequestId { get; }
    public string? Outcome { get; }
    public int? RetryAfterSeconds { get; }
    public Exception? DiagnosticException { get; }

    public static BundleUploadResponse Uploaded(string outcome, string? requestId) =>
        new(BundleUploadDisposition.Uploaded, outcome, null, requestId, outcome, null, null);

    public static BundleUploadResponse Equivalent(string code, string? requestId) =>
        new(BundleUploadDisposition.Equivalent, code, null, requestId, code, null, null);

    public static BundleUploadResponse Permanent(
        string code,
        string? detail = null,
        string? requestId = null
    ) => new(BundleUploadDisposition.Permanent, code, detail, requestId, null, null, null);

    public static BundleUploadResponse Transient(
        string code,
        string? detail = null,
        string? requestId = null,
        int? retryAfterSeconds = null,
        Exception? diagnosticException = null
    ) =>
        new(
            BundleUploadDisposition.Transient,
            code,
            detail,
            requestId,
            null,
            retryAfterSeconds,
            diagnosticException
        );
}
