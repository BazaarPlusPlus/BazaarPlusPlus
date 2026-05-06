#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online;

internal sealed class RunBundleClient
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;

    public RunBundleClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<RunBundleUploadResult> UploadRunBundleAsync(
        Models.RunBundleUploadRequestV3 payload,
        CancellationToken cancellationToken
    )
    {
        var bodyBytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(payload, V3Serialization.SerializerSettings)
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, _routes.UploadRunBundle)
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new("application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return RunBundleUploadResult.Success();

        var responseBody = await response.Content.ReadAsStringAsync();
        return RunBundleUploadResult.Failure(
            V3ErrorFormatter.FormatHttpFailure((int)response.StatusCode, responseBody)
        );
    }
}

internal readonly struct RunBundleUploadResult
{
    private RunBundleUploadResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static RunBundleUploadResult Success() => new(true, null);

    public static RunBundleUploadResult Failure(string error) => new(false, error);
}
