#nullable enable
using System.Text;
using BazaarPlusPlus.ModApi.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Clients;

internal sealed class BppAccountClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public BppAccountClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BppAccountBindResult> BindAsync(
        string bindCode,
        string gameToken,
        string gameAccountId,
        string platform,
        string flavor,
        CancellationToken cancellationToken
    )
    {
        var payload = new JObject
        {
            ["bind_code"] = bindCode,
            ["game_token"] = gameToken,
            ["game_account_id"] = gameAccountId,
            ["platform"] = platform,
            ["flavor"] = flavor,
        };
        var (parsed, transportError) = await PostJsonAsync(
                _routes.AccountBind,
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
        return ParseBindResult(parsed, transportError);
    }

    private async Task<(ModApiResponse? Response, string? TransportError)> PostJsonAsync(
        string url,
        JObject payload,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var parsed = await ModApiResponse
                .ReadAsync(response, ModApiBodyReadPolicy.Json, timeout.Token)
                .ConfigureAwait(false);
            return (parsed, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "request_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, "transport_error");
        }
    }

    private static BppAccountBindResult ParseBindResult(
        ModApiResponse? parsed,
        string? transportError
    )
    {
        if (parsed == null)
            return BppAccountBindResult.Failure(transportError ?? "transport_error");

        if (parsed.IsSuccess && parsed.StatusCode == 200)
        {
            try
            {
                var body = JObject.Parse(parsed.Body);
                var userName = ReadString(body, "user_name");
                var emailMasked = ReadString(body, "email_masked");
                if (userName != null && emailMasked != null)
                    return BppAccountBindResult.Success(userName, emailMasked);
            }
            catch { }
            return BppAccountBindResult.Failure("invalid_response", parsed.StatusCode);
        }

        return BppAccountBindResult.Failure(ResolveErrorCode(parsed), parsed.StatusCode);
    }

    private static string ResolveErrorCode(ModApiResponse response) =>
        response.Error.Shape == ModApiEnvelopeShape.Nested
        && !string.IsNullOrWhiteSpace(response.Error.Code)
            ? response.Error.Code!
            : response.UserCode;

    private static string? ReadString(JObject source, string name)
    {
        var value =
            source[name]?.Type == JTokenType.String ? source[name]?.Value<string>()?.Trim() : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

public readonly struct BppAccountBindResult
{
    private BppAccountBindResult(
        bool succeeded,
        string? userName,
        string? emailMasked,
        string? errorCode,
        int? statusCode
    )
    {
        Succeeded = succeeded;
        UserName = userName;
        EmailMasked = emailMasked;
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public bool Succeeded { get; }
    public string? UserName { get; }
    public string? EmailMasked { get; }
    public string? ErrorCode { get; }
    public int? StatusCode { get; }

    public static BppAccountBindResult Success(string userName, string emailMasked) =>
        new(true, userName, emailMasked, null, 200);

    public static BppAccountBindResult Failure(string errorCode, int? statusCode = null) =>
        new(false, null, null, errorCode, statusCode);
}
