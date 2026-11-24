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

public sealed record AndroidAdaptedGame(
    string Source,
    string GameVersion,
    long VersionCode,
    string Il2CppSha256,
    string MetadataSha256
);

public sealed record AndroidAppRelease(
    IReadOnlyList<AndroidAdaptedGame> Games,
    Uri AppUrl,
    long AppSize,
    string AppSha256
);

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

public sealed class ReleaseManifestClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string? _platformKey;
    private Remembered? _remembered;

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

    // release/manifest.ts owns this schema. Incomplete Managed Payload metadata makes callers fall back to the installer.
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
            digest!,
            ReadChunkIndex(payload["chunkIndex"] as JObject, uri)
        );
    }

    // Malformed or cross-origin chunkIndex metadata degrades gracefully to full-zip staging.
    private static ManagedPayloadChunkIndexReference? ReadChunkIndex(
        JObject? chunkIndex,
        Uri managedPayloadUri
    )
    {
        if (chunkIndex == null)
            return null;
        var url = chunkIndex.Value<string>("url");
        var size =
            chunkIndex["size"]?.Type == JTokenType.Integer ? chunkIndex.Value<long>("size") : 0;
        var sha256 = chunkIndex.Value<string>("sha256");
        var signature = chunkIndex.Value<string>("signature");
        var chunksBaseUrl = chunkIndex.Value<string>("chunksBaseUrl");
        if (
            !TryParseSameOriginHttpsUri(url, managedPayloadUri, out var indexUri)
            || !TryParseSameOriginHttpsUri(chunksBaseUrl, managedPayloadUri, out var chunksBaseUri)
            || size <= 0
            || size > ManagedPayloadChunkIndex.MaxIndexBytes
            || string.IsNullOrWhiteSpace(sha256)
            || !IsSha256(sha256!)
            || string.IsNullOrWhiteSpace(signature)
        )
            return null;
        return new ManagedPayloadChunkIndexReference(
            indexUri!,
            size,
            sha256!,
            signature!.Trim(),
            chunksBaseUri!
        );
    }

    private static bool TryParseSameOriginHttpsUri(string? raw, Uri expectedOrigin, out Uri? uri)
    {
        uri = null;
        if (
            !Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || !string.Equals(parsed.Host, expectedOrigin.Host, StringComparison.OrdinalIgnoreCase)
            || parsed.Port != expectedOrigin.Port
        )
            return false;
        uri = parsed;
        return true;
    }

    // release/manifest.ts owns the Android schema. The top-level game field serves older mods.
    private AndroidAppRelease? ReadAndroidApp(JObject document)
    {
        if (_platformKey == null || document["downloads"]?[_platformKey] is not JObject release)
            return null;
        if (release["app"] is not JObject apk || release["adaptations"] is not JObject adaptations)
            return null;
        var games = new List<AndroidAdaptedGame>();
        foreach (var adaptation in adaptations.Properties())
        {
            if (ReadAdaptedGame(adaptation) is not { } game)
                return null;
            games.Add(game);
        }
        var url = apk.Value<string>("url");
        var size = apk["size"]?.Type == JTokenType.Integer ? apk.Value<long>("size") : 0;
        var sha256 = apk["sha256"]?.Type == JTokenType.String ? apk.Value<string>("sha256")! : "";
        if (
            games.Count == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || size <= 0
            || !IsSha256(sha256)
        )
            return null;
        return new AndroidAppRelease(games, uri, size, sha256);
    }

    private static AndroidAdaptedGame? ReadAdaptedGame(JProperty adaptation)
    {
        if (adaptation.Value is not JObject entry || entry["game"] is not JObject game)
            return null;
        if (game["gameVersion"]?.Type != JTokenType.String)
            return null;
        var gameVersion = game.Value<string>("gameVersion")!.Trim();
        var versionCode =
            game["versionCode"]?.Type == JTokenType.Integer ? game.Value<long>("versionCode") : 0;
        var il2cpp =
            game["il2cppSha256"]?.Type == JTokenType.String
                ? game.Value<string>("il2cppSha256")!
                : "";
        var metadata =
            game["metadataSha256"]?.Type == JTokenType.String
                ? game.Value<string>("metadataSha256")!
                : "";
        if (gameVersion.Length == 0 || versionCode <= 0 || !IsSha256(il2cpp) || !IsSha256(metadata))
            return null;
        return new AndroidAdaptedGame(adaptation.Name, gameVersion, versionCode, il2cpp, metadata);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private sealed record Remembered(EntityTagHeaderValue ETag, ReleaseManifestFetchResult Result);
}
