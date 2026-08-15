#nullable enable
using Newtonsoft.Json;

namespace BazaarPlusPlus.Infrastructure.ReleaseManifest;

internal enum ReleaseManifestFailureKind
{
    None,
    Cancelled,
    HttpFailureStatus,
    ManifestVersionMissing,
    RequestTimedOut,
    RequestException,
}

internal readonly record struct ReleaseManifestFetchResult(
    string? Version,
    ReleaseManifestFailureKind FailureKind,
    int? HttpStatus,
    Exception? Exception
)
{
    internal bool Succeeded => FailureKind == ReleaseManifestFailureKind.None;

    internal static ReleaseManifestFetchResult Success(string version) =>
        new(version, ReleaseManifestFailureKind.None, null, null);

    internal static ReleaseManifestFetchResult Failure(
        ReleaseManifestFailureKind kind,
        int? httpStatus = null,
        Exception? exception = null
    ) => new(null, kind, httpStatus, exception);
}

internal sealed class ReleaseManifestClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;

    internal ReleaseManifestClient(HttpClient httpClient, Uri endpoint)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }

    internal async Task<ReleaseManifestFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync(_endpoint, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ReleaseManifestFetchResult.Failure(
                    ReleaseManifestFailureKind.HttpFailureStatus,
                    (int)response.StatusCode
                );
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var version = JsonConvert.DeserializeObject<ReleaseManifestDocument>(body)?.Version;
            return string.IsNullOrWhiteSpace(version)
                ? ReleaseManifestFetchResult.Failure(
                    ReleaseManifestFailureKind.ManifestVersionMissing
                )
                : ReleaseManifestFetchResult.Success(version.Trim());
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return ReleaseManifestFetchResult.Failure(
                ReleaseManifestFailureKind.Cancelled,
                exception: ex
            );
        }
        catch (TaskCanceledException ex)
        {
            return ReleaseManifestFetchResult.Failure(
                ReleaseManifestFailureKind.RequestTimedOut,
                exception: ex
            );
        }
        catch (Exception ex)
        {
            return ReleaseManifestFetchResult.Failure(
                ReleaseManifestFailureKind.RequestException,
                exception: ex
            );
        }
    }

    private sealed class ReleaseManifestDocument
    {
        [JsonProperty("version")]
        public string? Version { get; set; }
    }
}
