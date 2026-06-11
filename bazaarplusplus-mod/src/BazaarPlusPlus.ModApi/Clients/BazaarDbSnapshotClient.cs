#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class BazaarDbSnapshotClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public BazaarDbSnapshotClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<BazaarDbSnapshotUploadResult> UploadSnapshotAsync(
        BazaarDbSnapshotUploadRequest payload,
        CancellationToken cancellationToken
    )
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var route = _routes.CreateBazaarDbSnapshotUpload(payload.Snapshot.Id);
        var result = await ModApiJsonPost.PostJsonAsync(
            _httpClient,
            route,
            payload,
            cancellationToken
        );
        if (result.IsSuccess)
            return BazaarDbSnapshotUploadResult.Success();

        var formattedError = ModApiErrorFormatter.FormatHttpFailure(
            result.StatusCode,
            result.FailureBody!
        );
        return IsPermanentClientError(result.StatusCode)
            ? BazaarDbSnapshotUploadResult.PermanentFailure(formattedError)
            : BazaarDbSnapshotUploadResult.TransientFailure(formattedError);
    }

    private static bool IsPermanentClientError(int statusCode)
    {
        return statusCode >= 400 && statusCode < 500 && statusCode != 408 && statusCode != 429;
    }
}

public readonly struct BazaarDbSnapshotUploadResult
{
    private BazaarDbSnapshotUploadResult(bool succeeded, bool permanent, string? error)
    {
        Succeeded = succeeded;
        Permanent = permanent;
        Error = error;
    }

    public bool Succeeded { get; }

    public bool Permanent { get; }

    public string? Error { get; }

    public static BazaarDbSnapshotUploadResult Success() => new(true, false, null);

    public static BazaarDbSnapshotUploadResult TransientFailure(string error) =>
        new(false, false, error);

    public static BazaarDbSnapshotUploadResult PermanentFailure(string error) =>
        new(false, true, error);
}
