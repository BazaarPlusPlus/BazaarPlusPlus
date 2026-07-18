#nullable enable
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadService : IDisposable
{
    private readonly IRunBundleUploadStore _store;
    private readonly ModApiRoutes _routes;
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _playerAccountIdResolver;

    public RunBundleUploadService(RunBundleUploadStore store, ModApiRoutes routes, TimeSpan timeout)
        : this(
            store,
            routes,
            BppHttpClientFactory.Create(
                productVersion: BppPluginVersion.Current,
                userAgentSuffix: "RunBundleUpload",
                timeout: timeout
            ),
            BppClientCacheBridge.TryGetProfileAccountId
        ) { }

    internal RunBundleUploadService(
        IRunBundleUploadStore store,
        ModApiRoutes routes,
        HttpClient httpClient,
        Func<string?> playerAccountIdResolver
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _playerAccountIdResolver =
            playerAccountIdResolver
            ?? throw new ArgumentNullException(nameof(playerAccountIdResolver));
    }

    public Task<UploadAttemptResult> UploadPendingRunBundlesAsync(
        CancellationToken cancellationToken
    ) => UploadPendingRunBundlesAsync(ResolvePlayerAccountId(), cancellationToken);

    public Task<UploadAttemptResult> UploadPendingRunBundlesInBackgroundAsync(
        CancellationToken cancellationToken
    )
    {
        var playerAccountId = ResolvePlayerAccountId();
        return Task.Run(
            () => UploadPendingRunBundlesAsync(playerAccountId, cancellationToken),
            cancellationToken
        );
    }

    private async Task<UploadAttemptResult> UploadPendingRunBundlesAsync(
        string? playerAccountId,
        CancellationToken cancellationToken
    )
    {
        var pendingRunIds = _store.GetPendingCompletedRunIds(3);
        if (pendingRunIds.Count == 0)
            return UploadAttemptResult.NoWork();

        if (string.IsNullOrWhiteSpace(playerAccountId))
            return UploadAttemptResult.From(
                UploadAttemptObservation.Deferred(
                    UploadLogReasonCode.AccountUnavailable,
                    pendingRunIds.Count
                )
            );

        var client = new RunBundleClient(_httpClient, _routes);
        var observations = new List<UploadAttemptObservation>(pendingRunIds.Count);
        foreach (var runId in pendingRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            try
            {
                var buildResult = _store.BuildRunBundleSnapshot(runId, playerAccountId);
                var snapshot = buildResult.Snapshot;
                if (buildResult.Status == RunBundleBuildStatus.NotReady)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "run_bundle_not_ready");
                    observations.Add(
                        UploadAttemptObservation.Deferred(UploadLogReasonCode.RunBundleNotReady)
                    );
                    continue;
                }
                if (buildResult.Status == RunBundleBuildStatus.IntegrityFailed || snapshot == null)
                {
                    _store.MarkRunUploadFailed(
                        runId,
                        attemptedAtUtc,
                        buildResult.ReasonCode?.ToString() ?? "run_bundle_build_failed"
                    );
                    continue;
                }

                var result = await client.UploadRunBundleAsync(
                    snapshot.Metadata,
                    snapshot.ArtifactBytes,
                    cancellationToken
                );
                if (!result.Succeeded)
                {
                    var error = result.Error ?? "run_bundle_upload_failed";
                    if (result.Permanent)
                        _store.MarkRunUploadPermanentlyFailed(runId, attemptedAtUtc, error);
                    else
                        _store.MarkRunUploadFailed(runId, attemptedAtUtc, error);
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            runId,
                            UploadLogReasonCode.RemoteUploadFailed
                        )
                    );
                    continue;
                }

                _store.MarkRunUploaded(
                    runId,
                    snapshot.LastSeq,
                    snapshot.UploadedStatus,
                    snapshot.BattleIds,
                    DateTimeOffset.UtcNow
                );
                observations.Add(UploadAttemptObservation.Succeeded(runId));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkRunUploadFailed(runId, attemptedAtUtc, ex.Message);
                observations.Add(
                    UploadAttemptObservation.Degraded(
                        runId,
                        UploadLogReasonCode.AttemptException,
                        ex
                    )
                );
            }
        }

        return observations.Count == 0
            ? UploadAttemptResult.NoHealthSignal()
            : UploadAttemptResult.From(observations);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private string? ResolvePlayerAccountId()
    {
        try
        {
            return _playerAccountIdResolver()?.Trim();
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                UploadLogEvents.AccountProbeFailed,
                ex,
                () =>
                    [
                        UploadLogEvents.AccountProbeFailedFeed.Bind(UploadFeedKind.RunBundle),
                        UploadLogEvents.AccountProbeFailedReasonCode.Bind(
                            UploadLogReasonCode.AccountProbeException
                        ),
                    ]
            );
            return null;
        }
    }
}

internal interface IRunBundleUploadStore
{
    IReadOnlyList<string> GetPendingCompletedRunIds(int limit);
    RunBundleBuildResult BuildRunBundleSnapshot(string runId, string playerAccountId);
    void MarkRunUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error);
    void MarkRunUploadPermanentlyFailed(string runId, DateTimeOffset attemptedAtUtc, string error);
    void MarkRunUploaded(
        string runId,
        long uploadedSeq,
        string? uploadedStatus,
        IReadOnlyList<string> battleIds,
        DateTimeOffset uploadedAtUtc
    );
}
