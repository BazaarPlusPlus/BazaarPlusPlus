#nullable enable
using System.Text;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Clients;

public enum BazaarDbLinkOutcome
{
    Linked,
    InvalidOrExpired,
    AlreadyLinked,
    MissingFields,
    ServerError,
    Transport,
}

public readonly struct BazaarDbLinkResult
{
    private BazaarDbLinkResult(BazaarDbLinkOutcome outcome, int? statusCode, ModApiFailure? failure)
    {
        Outcome = outcome;
        StatusCode = statusCode;
        Failure = failure;
    }

    public BazaarDbLinkOutcome Outcome { get; }

    public int? StatusCode { get; }

    public ModApiFailure? Failure { get; }
    public string? Error => Failure?.UserCode;
    public Exception? DiagnosticException => Failure?.DiagnosticException;

    public bool Succeeded => Outcome == BazaarDbLinkOutcome.Linked;

    public static BazaarDbLinkResult Linked(int statusCode) =>
        new(BazaarDbLinkOutcome.Linked, statusCode, null);

    public static BazaarDbLinkResult From(BazaarDbLinkOutcome o, int? status, string? error) =>
        new(o, status, error == null ? null : new ModApiFailure(error));

    internal static BazaarDbLinkResult Failed(
        BazaarDbLinkOutcome outcome,
        int? statusCode,
        ModApiFailure failure
    ) => new(outcome, statusCode, failure);
}

/// <summary>
/// Redeems a one-time BazaarDB profile-link code. Posts to a fixed full URI, no auth header.
/// Code is trimmed only (case-sensitive alphabet). 409 means already linked to a different
/// BazaarDB user and is permanent.
/// </summary>
public sealed class BazaarDbLinkClient : IDisposable
{
    public const string DefaultRedeemEndpoint = "https://bazaardb.gg/api/profile/link/redeem";

    private readonly HttpClient _httpClient;
    private readonly Uri _redeemEndpoint;

    public BazaarDbLinkClient(HttpClient httpClient, Uri redeemEndpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _redeemEndpoint = redeemEndpoint ?? throw new ArgumentNullException(nameof(redeemEndpoint));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<BazaarDbLinkResult> RedeemAsync(
        string code,
        string accountId,
        CancellationToken cancellationToken
    )
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0 || string.IsNullOrWhiteSpace(accountId))
            return BazaarDbLinkResult.From(BazaarDbLinkOutcome.MissingFields, 400, "missing_field");

        var payload = new BazaarDbProfileLinkRedeemRequest
        {
            Code = trimmedCode,
            AccountId = accountId,
        };
        try
        {
            var bodyBytes = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(payload, ModApiSerialization.SerializerSettings)
            );
            using var request = new HttpRequestMessage(HttpMethod.Post, _redeemEndpoint)
            {
                Content = new ByteArrayContent(bodyBytes),
            };
            request.Content.Headers.ContentType = new("application/json");
            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var result = await ModApiResponse
                .ReadAsync(response, ModApiBodyReadPolicy.Json, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
                return BazaarDbLinkResult.Linked(result.StatusCode);

            return Classify(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BazaarDbLinkResult.Failed(
                BazaarDbLinkOutcome.Transport,
                null,
                new ModApiFailure("transport_error", diagnosticException: ex)
            );
        }
    }

    private static BazaarDbLinkResult Classify(ModApiResponse response)
    {
        var statusCode = response.StatusCode;
        var error = response.Error.Code;

        if (error == "already_linked" || statusCode == 409)
            return BazaarDbLinkResult.Failed(
                BazaarDbLinkOutcome.AlreadyLinked,
                statusCode,
                new ModApiFailure("already_linked", response: response)
            );
        if (error == "invalid_or_expired")
            return BazaarDbLinkResult.Failed(
                BazaarDbLinkOutcome.InvalidOrExpired,
                statusCode,
                new ModApiFailure("invalid_or_expired", response: response)
            );
        if (IsMissingFieldError(error))
            return BazaarDbLinkResult.Failed(
                BazaarDbLinkOutcome.MissingFields,
                statusCode,
                new ModApiFailure("missing_field", response: response)
            );
        if (statusCode >= 500)
            return BazaarDbLinkResult.Failed(
                BazaarDbLinkOutcome.ServerError,
                statusCode,
                new ModApiFailure("server_error", response: response)
            );

        return BazaarDbLinkResult.Failed(
            BazaarDbLinkOutcome.InvalidOrExpired,
            statusCode,
            new ModApiFailure("invalid_or_expired", response: response)
        );
    }

    private static bool IsMissingFieldError(string? error)
    {
        if (error == null)
            return false;

        return error.StartsWith("Missing", StringComparison.Ordinal)
            || error.StartsWith("missing_", StringComparison.OrdinalIgnoreCase);
    }
}
