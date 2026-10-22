#nullable enable
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Infrastructure.ReleaseManifest;

public enum ReleaseManifestFailureKind
{
    None,
    Cancelled,
    HttpFailureStatus,
    ManifestVersionMissing,
    RequestTimedOut,
    RequestException,
}

/// <summary>
/// One read of a Release Manifest: the published product version and, from a Platform Release
/// Manifest's <c>downloads[platform]</c>, that platform's Managed Payload when it is complete.
/// </summary>
public readonly record struct ReleaseManifestFetchResult(
    string? Version,
    ManagedPayloadRelease? ManagedPayload,
    ReleaseManifestFailureKind FailureKind,
    int? HttpStatus,
    Exception? Exception
)
{
    public bool Succeeded => FailureKind == ReleaseManifestFailureKind.None;

    public static ReleaseManifestFetchResult Success(
        string version,
        ManagedPayloadRelease? managedPayload = null
    ) => new(version, managedPayload, ReleaseManifestFailureKind.None, null, null);

    public static ReleaseManifestFetchResult Failure(
        ReleaseManifestFailureKind kind,
        int? httpStatus = null,
        Exception? exception = null
    ) => new(null, null, kind, httpStatus, exception);
}

public sealed class ReleaseManifestClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string? _platformKey;

    /// <summary>
    /// <paramref name="platformKey"/> names the platform whose Managed Payload to read; null
    /// reads only the version.
    /// </summary>
    public ReleaseManifestClient(HttpClient httpClient, Uri endpoint, string? platformKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _platformKey = platformKey;
    }

    public async Task<ReleaseManifestFetchResult> FetchAsync(CancellationToken cancellationToken)
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
            var document = JsonConvert.DeserializeObject<JObject>(body);
            var version = document?.Value<string>("version");
            return string.IsNullOrWhiteSpace(version)
                ? ReleaseManifestFetchResult.Failure(
                    ReleaseManifestFailureKind.ManifestVersionMissing
                )
                : ReleaseManifestFetchResult.Success(
                    version!.Trim(),
                    ReadManagedPayload(document!, version!.Trim())
                );
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

    // release/manifest.mjs owns these fields; a record that is incomplete here is no Managed
    // Payload, so the caller falls back to the installer.
    private ManagedPayloadRelease? ReadManagedPayload(JObject document, string version)
    {
        if (_platformKey == null || document["downloads"]?[_platformKey] is not JObject release)
            return null;
        if (release["managedPayload"] is not JObject payload)
            return null;

        var url = payload.Value<string>("url");
        var size = payload["size"]?.Type == JTokenType.Integer ? payload.Value<long>("size") : 0;
        var sha256 = payload.Value<string>("sha256");
        var signature = payload.Value<string>("signature");
        var historyVersion =
            release["historyDatabaseUserVersion"]?.Type == JTokenType.Integer
                ? release.Value<int>("historyDatabaseUserVersion")
                : 0;
        var digest = release.Value<string>("nonManagedPayloadDigest");
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || size <= 0
            || string.IsNullOrWhiteSpace(sha256)
            || string.IsNullOrWhiteSpace(signature)
            || historyVersion <= 0
            || string.IsNullOrWhiteSpace(digest)
        )
            return null;

        return new ManagedPayloadRelease(
            version,
            uri,
            size,
            sha256!,
            signature!,
            historyVersion,
            digest!
        );
    }
}
