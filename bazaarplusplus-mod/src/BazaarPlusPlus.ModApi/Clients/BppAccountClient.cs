#nullable enable
using System.Net.Http.Headers;
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

    public async Task<BppAccountDeviceResult> BindAsync(
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
        return ParseDeviceResult(parsed, transportError);
    }

    public async Task<BppAccountDeviceResult> IssueDeviceTokenAsync(
        string gameToken,
        string gameAccountId,
        string platform,
        string flavor,
        CancellationToken cancellationToken
    )
    {
        var payload = new JObject
        {
            ["game_token"] = gameToken,
            ["game_account_id"] = gameAccountId,
            ["platform"] = platform,
            ["flavor"] = flavor,
        };
        var (parsed, transportError) = await PostJsonAsync(
                _routes.AccountDevices,
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
        return ParseDeviceResult(parsed, transportError);
    }

    public async Task<BppAccountMeResult> GetMeAsync(
        string deviceToken,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
            return BppAccountMeResult.Unauthorized();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _routes.AccountMe);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                deviceToken.Trim()
            );
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var parsed = await ModApiResponse
                .ReadAsync(response, ModApiBodyReadPolicy.Json, timeout.Token)
                .ConfigureAwait(false);
            if (parsed.StatusCode == 401)
                return BppAccountMeResult.Unauthorized();
            if (parsed.IsSuccess && parsed.StatusCode == 200)
            {
                try
                {
                    var body = JObject.Parse(parsed.Body);
                    var userName = ReadString(body, "user_name");
                    var emailMasked = ReadString(body, "email_masked");
                    if (userName != null && emailMasked != null)
                        return BppAccountMeResult.Success(userName, emailMasked);
                }
                catch { }
                return BppAccountMeResult.Failure("invalid_response", parsed.StatusCode);
            }
            return BppAccountMeResult.Failure(ResolveErrorCode(parsed), parsed.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BppAccountMeResult.Failure("request_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return BppAccountMeResult.Failure("transport_error");
        }
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

    private static BppAccountDeviceResult ParseDeviceResult(
        ModApiResponse? parsed,
        string? transportError
    )
    {
        if (parsed == null)
            return BppAccountDeviceResult.Failure(transportError ?? "transport_error");

        if (parsed.IsSuccess && parsed.StatusCode == 200)
        {
            try
            {
                var body = JObject.Parse(parsed.Body);
                var deviceToken = ReadString(body, "device_token");
                var userName = ReadString(body, "user_name");
                var emailMasked = ReadString(body, "email_masked");
                if (
                    deviceToken != null
                    && deviceToken.StartsWith("bppd_", StringComparison.Ordinal)
                    && userName != null
                    && emailMasked != null
                )
                    return BppAccountDeviceResult.Success(deviceToken, userName, emailMasked);
            }
            catch { }
            return BppAccountDeviceResult.Failure("invalid_response", parsed.StatusCode);
        }

        return BppAccountDeviceResult.Failure(ResolveErrorCode(parsed), parsed.StatusCode);
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

public readonly struct BppAccountDeviceResult
{
    private BppAccountDeviceResult(
        bool succeeded,
        string? deviceToken,
        string? userName,
        string? emailMasked,
        string? errorCode,
        int? statusCode
    )
    {
        Succeeded = succeeded;
        DeviceToken = deviceToken;
        UserName = userName;
        EmailMasked = emailMasked;
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public bool Succeeded { get; }
    public string? DeviceToken { get; }
    public string? UserName { get; }
    public string? EmailMasked { get; }
    public string? ErrorCode { get; }
    public int? StatusCode { get; }

    public static BppAccountDeviceResult Success(
        string deviceToken,
        string userName,
        string emailMasked
    ) => new(true, deviceToken, userName, emailMasked, null, 200);

    public static BppAccountDeviceResult Failure(string errorCode, int? statusCode = null) =>
        new(false, null, null, null, errorCode, statusCode);
}

public readonly struct BppAccountMeResult
{
    private BppAccountMeResult(
        bool succeeded,
        bool isUnauthorized,
        string? userName,
        string? emailMasked,
        string? errorCode,
        int? statusCode
    )
    {
        Succeeded = succeeded;
        IsUnauthorized = isUnauthorized;
        UserName = userName;
        EmailMasked = emailMasked;
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public bool Succeeded { get; }
    public bool IsUnauthorized { get; }
    public string? UserName { get; }
    public string? EmailMasked { get; }
    public string? ErrorCode { get; }
    public int? StatusCode { get; }

    public static BppAccountMeResult Success(string userName, string emailMasked) =>
        new(true, false, userName, emailMasked, null, 200);

    public static BppAccountMeResult Unauthorized() =>
        new(false, true, null, null, "unauthorized", 401);

    public static BppAccountMeResult Failure(string errorCode, int? statusCode = null) =>
        new(false, false, null, null, errorCode, statusCode);
}
