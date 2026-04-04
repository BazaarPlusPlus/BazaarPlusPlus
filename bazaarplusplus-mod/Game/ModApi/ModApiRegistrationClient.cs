#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiRegistrationClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiClientStateStore _clientStateStore;
    private readonly ModApiKeyStore _keyStore;
    private readonly string _registrationEndpoint;

    public ModApiRegistrationClient(
        HttpClient httpClient,
        ModApiClientStateStore clientStateStore,
        ModApiKeyStore keyStore,
        string registrationEndpoint
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        if (string.IsNullOrWhiteSpace(registrationEndpoint))
            throw new ArgumentException(
                "Registration endpoint is required.",
                nameof(registrationEndpoint)
            );

        _registrationEndpoint = registrationEndpoint;
    }

    public async Task<string?> EnsureClientRegistrationAsync(
        string installId,
        CancellationToken cancellationToken
    )
    {
        var existingClientId = _clientStateStore.TryGetClientId();
        if (!string.IsNullOrWhiteSpace(existingClientId))
        {
            BppLog.Info("ModApiRegistrationClient", "Using cached client id.");
            return existingClientId;
        }

        try
        {
            var keyMaterial = _keyStore.GetOrCreateKeyMaterial();
            var pluginVersion = BppPluginVersion.Current;
            BppLog.Info("ModApiRegistrationClient", "Registering client.");
            var requestBody = JsonConvert.SerializeObject(
                new JObject
                {
                    ["install_id"] = installId,
                    ["plugin_version"] = pluginVersion,
                    ["requested_at_utc"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["public_key"] = JToken.FromObject(keyMaterial.ToPublicKey()),
                },
                ModApiSerialization.SerializerSettings
            );
            using var request = new HttpRequestMessage(HttpMethod.Post, _registrationEndpoint)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-BPP-Install-Id", installId);
            request.Headers.TryAddWithoutValidation("X-BPP-Plugin-Version", pluginVersion);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                BppLog.Warn(
                    "ModApiRegistrationClient",
                    $"Client registration failed: {(int)response.StatusCode} - {ModApiErrorFormatter.Truncate(responseBody)}"
                );
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var payload = JObject.Parse(responseJson);
            var clientId = payload["client_id"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                BppLog.Warn(
                    "ModApiRegistrationClient",
                    "Client registration response did not contain client_id."
                );
                return null;
            }

            _clientStateStore.SaveClientId(clientId);
            BppLog.Info("ModApiRegistrationClient", "Client registration succeeded.");
            return clientId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "ModApiRegistrationClient",
                $"Client registration failed for endpoint={_registrationEndpoint}: {FormatException(ex)}"
            );
            return null;
        }
    }

    private static string FormatException(Exception ex)
    {
        var message = $"{ex.GetType().Name} - {ex.Message}";
        if (ex.InnerException == null)
            return message;

        return $"{message} | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
    }
}
