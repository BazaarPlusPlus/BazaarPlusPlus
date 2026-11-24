#nullable enable
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class ModApiSession : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly BundleUploadClient _bundleUpload;
    private readonly GhostBattleClient _ghostBattles;
    private readonly ModApiHealthClient _health;
    private readonly BppAccountClient _accounts;
    private int _disposed;

    private ModApiSession(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient;
        _bundleUpload = new BundleUploadClient(httpClient, routes);
        _ghostBattles = new GhostBattleClient(httpClient, routes);
        _health = new ModApiHealthClient(httpClient, routes);
        _accounts = new BppAccountClient(httpClient, routes);
    }

    public static ModApiSession? TryCreate(
        string? apiBaseUrl,
        string productVersion,
        string? platformKey,
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
            platformKey,
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
        CancellationToken cancellationToken,
        string? deviceToken = null
    ) =>
        _bundleUpload.UploadAsync(
            sealedFile,
            contentLength,
            contentDigest,
            expectedBundleId,
            expectedRunId,
            cancellationToken,
            deviceToken
        );

    public Task<GhostBattleQueryResult> QueryGhostBattlesAgainstMeAsync(
        string playerAccountId,
        int limit,
        CancellationToken cancellationToken,
        string? deviceToken = null
    ) => _ghostBattles.QueryAgainstMeAsync(playerAccountId, limit, cancellationToken, deviceToken);

    public Task<GhostBundleDownloadResult> DownloadGhostBundleAsync(
        string downloadUrl,
        CancellationToken cancellationToken
    ) => _ghostBattles.DownloadBundleAsync(downloadUrl, cancellationToken);

    public Task<ModApiHealthProbeResult> ProbeHealthAsync(CancellationToken cancellationToken) =>
        _health.ProbeAsync(cancellationToken);

    public Task<BppAccountBindStartResult> StartBppAccountBindAsync(
        string email,
        string gameToken,
        string gameAccountId,
        string platform,
        string flavor,
        CancellationToken cancellationToken
    ) =>
        _accounts.StartBindAsync(
            email,
            gameToken,
            gameAccountId,
            platform,
            flavor,
            cancellationToken
        );

    public Task<BppAccountDeviceResult> ConfirmBppAccountBindAsync(
        string pendingId,
        string code,
        CancellationToken cancellationToken
    ) => _accounts.ConfirmBindAsync(pendingId, code, cancellationToken);

    public Task<BppAccountDeviceResult> IssueBppAccountDeviceTokenAsync(
        string gameToken,
        string gameAccountId,
        string platform,
        string flavor,
        CancellationToken cancellationToken
    ) =>
        _accounts.IssueDeviceTokenAsync(
            gameToken,
            gameAccountId,
            platform,
            flavor,
            cancellationToken
        );

    public Task<BppAccountMeResult> GetBppAccountMeAsync(
        string deviceToken,
        CancellationToken cancellationToken
    ) => _accounts.GetMeAsync(deviceToken, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _httpClient.Dispose();
    }
}
