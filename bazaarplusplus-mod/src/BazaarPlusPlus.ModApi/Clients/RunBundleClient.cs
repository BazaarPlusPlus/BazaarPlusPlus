#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        return RunBundleUploadResult.Failure(
            ModApiErrorFormatter.FormatHttpFailure((int)response.StatusCode, responseBody)
        );
    }
}

public readonly struct RunBundleUploadResult
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
