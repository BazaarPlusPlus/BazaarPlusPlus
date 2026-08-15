#nullable enable
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class ModApiSession : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly BundleUploadClient _bundleUpload;
    private readonly GhostBattleClient _ghostBattles;
    private readonly ModApiHealthClient _health;
    private int _disposed;

    private ModApiSession(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient;
        _bundleUpload = new BundleUploadClient(httpClient, routes);
        _ghostBattles = new GhostBattleClient(httpClient, routes);
        _health = new ModApiHealthClient(httpClient, routes);
    }

    public static ModApiSession? TryCreate(
        string? apiBaseUrl,
        string productVersion,
        string userAgentSuffix,
        TimeSpan timeout,
        HttpMessageHandler? handler = null
    )
    {
        var routes = ModApiRoutes.TryCreate(apiBaseUrl);
        if (routes == null)
            return null;
        var httpClient = BppHttpClientFactory.Create(
            productVersion,
            userAgentSuffix,
            timeout,
            handler
        );
        return new ModApiSession(httpClient, routes);
    }

    public Task<BundleUploadResponse> UploadBundleAsync(
        Stream sealedFile,
        long contentLength,
        string contentDigest,
        string expectedBundleId,
        string expectedRunId,
        CancellationToken cancellationToken
    ) =>
        _bundleUpload.UploadAsync(
            sealedFile,
            contentLength,
            contentDigest,
            expectedBundleId,
            expectedRunId,
            cancellationToken
        );

    public Task<GhostBattleQueryResult> QueryGhostBattlesAgainstMeAsync(
        string playerAccountId,
        int limit,
        CancellationToken cancellationToken
    ) => _ghostBattles.QueryAgainstMeAsync(playerAccountId, limit, cancellationToken);

    public Task<GhostBundleDownloadResult> DownloadGhostBundleAsync(
        string downloadUrl,
        CancellationToken cancellationToken
    ) => _ghostBattles.DownloadBundleAsync(downloadUrl, cancellationToken);

    public Task<ModApiHealthProbeResult> ProbeHealthAsync(CancellationToken cancellationToken) =>
        _health.ProbeAsync(cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _httpClient.Dispose();
    }
}
