#nullable enable
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class RunBundleClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public RunBundleClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<RunBundleUploadResult> UploadRunBundleAsync(
        RunBundleUploadRequest metadata,
        byte[] artifactBytes,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _routes.UploadRunBundle)
        {
            Content = RunBundleMultipartContent.Create(metadata, artifactBytes),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return RunBundleUploadResult.Success();

        var responseBody = await response.Content.ReadAsStringAsync();
        var statusCode = (int)response.StatusCode;
        var error = ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody);
        return IsPermanentClientError(statusCode)
            ? RunBundleUploadResult.PermanentFailure(error)
            : RunBundleUploadResult.TransientFailure(error);
    }

    private static bool IsPermanentClientError(int statusCode) =>
        statusCode >= 400 && statusCode < 500 && statusCode != 408 && statusCode != 429;
}

public readonly struct RunBundleUploadResult
{
    private RunBundleUploadResult(bool succeeded, bool permanent, string? error)
    {
        Succeeded = succeeded;
        Permanent = permanent;
        Error = error;
    }

    public bool Succeeded { get; }

    public bool Permanent { get; }

    public string? Error { get; }

    public static RunBundleUploadResult Success() => new(true, false, null);

    public static RunBundleUploadResult TransientFailure(string error) => new(false, false, error);

    public static RunBundleUploadResult PermanentFailure(string error) => new(false, true, error);
}
