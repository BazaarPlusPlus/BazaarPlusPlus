#nullable enable
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Http;

/// <summary>
/// Shared plumbing for the "serialize JSON body -> POST route -> read failure body" shape
/// used by the snapshot upload client. The success/failure result mapping stays with each client.
/// </summary>
public static class ModApiJsonPost
{
    public static async Task<ModApiJsonPostResult> PostJsonAsync<TPayload>(
        HttpClient httpClient,
        string route,
        TPayload payload,
        CancellationToken cancellationToken
    )
    {
        var bodyBytes = Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(payload, ModApiSerialization.SerializerSettings)
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return ModApiJsonPostResult.SuccessResult((int)response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        return ModApiJsonPostResult.FailureResult((int)response.StatusCode, responseBody);
    }
}

/// <summary>
/// Outcome of <see cref="ModApiJsonPost.PostJsonAsync{TPayload}"/>. The failure body is read
/// only when the response is not a success status code, matching the original per-client flow.
/// </summary>
public readonly struct ModApiJsonPostResult
{
    private ModApiJsonPostResult(bool isSuccess, int statusCode, string? failureBody)
    {
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        FailureBody = failureBody;
    }

    public bool IsSuccess { get; }

    public int StatusCode { get; }

    public string? FailureBody { get; }

    public static ModApiJsonPostResult SuccessResult(int statusCode) => new(true, statusCode, null);

    public static ModApiJsonPostResult FailureResult(int statusCode, string failureBody) =>
        new(false, statusCode, failureBody);
}
