#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiPlayerBindingClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRequestSigner _requestSigner;
    private readonly string _bindEndpoint;

    public ModApiPlayerBindingClient(
        HttpClient httpClient,
        ModApiRequestSigner requestSigner,
        string bindEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        if (string.IsNullOrWhiteSpace(bindEndpoint))
            throw new ArgumentException("Bind endpoint is required.", nameof(bindEndpoint));

        _bindEndpoint = bindEndpoint;
    }

    public async Task<ModApiPlayerBindingResult> BindPlayerAccountAsync(
        string clientId,
        string installId,
        string playerAccountId,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(playerAccountId))
            return ModApiPlayerBindingResult.Failure(
                "player_account_id_required",
                shouldFallback: false,
                shouldReRegister: false
            );

        try
        {
            var json = JsonConvert.SerializeObject(
                new JObject
                {
                    ["player_account_id"] = playerAccountId.Trim(),
                    ["observed_player_account_id"] = playerAccountId.Trim(),
                },
                ModApiSerialization.SerializerSettings
            );

            using var request = _requestSigner.CreateSignedRequest(
                HttpMethod.Post,
                _bindEndpoint,
                json,
                clientId,
                installId,
                "application/json"
            );
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (response.IsSuccessStatusCode)
                return ModApiPlayerBindingResult.Success();

            var responseBody = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;
            return ModApiPlayerBindingResult.Failure(
                ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody),
                shouldFallback: statusCode >= 500 || statusCode == 429,
                shouldReRegister: statusCode == 401
                    || statusCode == 403
                    || (
                        statusCode == 404
                        && ModApiErrorFormatter.IndicatesMissingClient(responseBody)
                    )
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "ModApiPlayerBindingClient",
                $"Bind request failed for endpoint={_bindEndpoint}: {ex.GetType().Name} - {ex.Message}"
            );
            return ModApiPlayerBindingResult.Failure(
                ModApiErrorFormatter.Truncate(ex.Message),
                shouldFallback: true,
                shouldReRegister: false
            );
        }
    }
}

internal readonly struct ModApiPlayerBindingResult : IModApiAuthenticatedResult
{
    private ModApiPlayerBindingResult(
        bool succeeded,
        string? error,
        bool shouldFallback,
        bool shouldReRegister
    )
    {
        Succeeded = succeeded;
        Error = error;
        ShouldFallback = shouldFallback;
        ShouldReRegister = shouldReRegister;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public bool ShouldFallback { get; }

    public bool ShouldReRegister { get; }

    public static ModApiPlayerBindingResult Success() => new(true, null, false, false);

    public static ModApiPlayerBindingResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, error, shouldFallback, shouldReRegister);
}
