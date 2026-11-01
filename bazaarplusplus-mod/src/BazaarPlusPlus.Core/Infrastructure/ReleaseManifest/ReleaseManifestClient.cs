#nullable enable
using System.Net;
using System.Net.Http.Headers;
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
/// What an Android Platform Release Manifest names for the Android App (root ADR 0012): the game
/// version this release was adapted for, as the game reports it in <c>Application.version</c>,
/// and the Android App APK to install for it, with the size and SHA-256 a download is checked
/// against.
/// </summary>
public sealed record AndroidAppRelease(
    string GameVersion,
    Uri AppUrl,
    long AppSize,
    string AppSha256
);

/// <summary>
/// One read of a Release Manifest: the published product version and, from a Platform Release
/// Manifest's <c>downloads[platform]</c>, that platform's Managed Payload when it is complete and
/// its Android App release when the platform has one.
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

    public AndroidAppRelease? AndroidApp { get; init; }

    public static ReleaseManifestFetchResult Success(
        string version,
        ManagedPayloadRelease? managedPayload = null,
        AndroidAppRelease? androidApp = null
    ) =>
        new(version, managedPayload, ReleaseManifestFailureKind.None, null, null)
        {
            AndroidApp = androidApp,
        };

    public static ReleaseManifestFetchResult Failure(
        ReleaseManifestFailureKind kind,
        int? httpStatus = null,
        Exception? exception = null
    ) => new(null, null, kind, httpStatus, exception);
}

/// <summary>
/// Reads one Release Manifest endpoint. A client kept across checks remembers the last
/// successful answer with its <c>ETag</c> and asks with <c>If-None-Match</c>, so an unchanged
/// manifest comes back as <c>304</c> and the remembered answer is returned; every check still
/// asks the server.
/// </summary>
public sealed class ReleaseManifestClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string? _platformKey;
    private Remembered? _remembered;

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
            var remembered = Volatile.Read(ref _remembered);
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            if (remembered != null)
                request.Headers.IfNoneMatch.Add(remembered.ETag);
            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && remembered != null)
                return remembered.Result;
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
            if (string.IsNullOrWhiteSpace(version))
                return ReleaseManifestFetchResult.Failure(
                    ReleaseManifestFailureKind.ManifestVersionMissing
                );
            var result = ReleaseManifestFetchResult.Success(
                version!.Trim(),
                ReadManagedPayload(document!, version!.Trim()),
                ReadAndroidApp(document!)
            );
            Volatile.Write(
                ref _remembered,
                response.Headers.ETag is { } etag ? new Remembered(etag, result) : null
            );
            return result;
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

    // An Android record names the game it was adapted for (game.gameVersion, the string the
    // running game reports) and the Android App APK (app.url, size, sha256); release/manifest.mjs
    // owns them. Anything incomplete is no release, and so is the retired A shape (apk, no app).
    private AndroidAppRelease? ReadAndroidApp(JObject document)
    {
        if (_platformKey == null || document["downloads"]?[_platformKey] is not JObject release)
            return null;
        if (release["app"] is not JObject apk || release["game"] is not JObject game)
            return null;
        if (game["gameVersion"]?.Type != JTokenType.String)
            return null;
        var gameVersion = game.Value<string>("gameVersion")!.Trim();
        var url = apk.Value<string>("url");
        var size = apk["size"]?.Type == JTokenType.Integer ? apk.Value<long>("size") : 0;
        var sha256 = apk["sha256"]?.Type == JTokenType.String ? apk.Value<string>("sha256")! : "";
        if (
            gameVersion.Length == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || size <= 0
            || !IsSha256(sha256)
        )
            return null;
        return new AndroidAppRelease(gameVersion, uri, size, sha256);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    // The last successful answer and the validator it came with, replaced as one.
    private sealed record Remembered(EntityTagHeaderValue ETag, ReleaseManifestFetchResult Result);
}
