#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadService : IDisposable
{
    private readonly RunBundleUploadStore _store;
    private readonly ModApiRoutes _routes;
    private readonly HttpClient _httpClient;

    public RunBundleUploadService(RunBundleUploadStore store, ModApiRoutes routes, TimeSpan timeout)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "RunBundleUpload",
            timeout: timeout
        );
    }

    public Task UploadPendingRunBundlesAsync(CancellationToken cancellationToken) =>
        UploadPendingRunBundlesAsync(ResolvePlayerAccountId(), cancellationToken);

    public Task UploadPendingRunBundlesInBackgroundAsync(CancellationToken cancellationToken)
    {
        var playerAccountId = ResolvePlayerAccountId();
        return Task.Run(
            () => UploadPendingRunBundlesAsync(playerAccountId, cancellationToken),
            cancellationToken
        );
    }

    private async Task UploadPendingRunBundlesAsync(
        string? playerAccountId,
        CancellationToken cancellationToken
    )
    {
        var pendingRunIds = _store.GetPendingCompletedRunIds(3);
        if (pendingRunIds.Count == 0)
        {
            BppLog.Info(
                "RunBundleUploadService",
                "No completed runs are waiting for bundle upload."
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(playerAccountId))
        {
            BppLog.Info(
                "RunBundleUploadService",
                $"Skipping {pendingRunIds.Count} pending run bundle(s): player account id not yet available."
            );
            return;
        }

        var client = new RunBundleClient(_httpClient, _routes);
        foreach (var runId in pendingRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            try
            {
                var snapshot = _store.TryBuildRunBundleSnapshot(runId, playerAccountId);
                if (snapshot == null)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "run_bundle_not_ready");
                    continue;
                }

                var result = await client.UploadRunBundleAsync(
                    snapshot.Metadata,
                    snapshot.ArtifactBytes,
                    cancellationToken
                );
                if (!result.Succeeded)
                {
                    _store.MarkRunUploadFailed(
                        runId,
                        attemptedAtUtc,
                        result.Error ?? "run_bundle_upload_failed"
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
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkRunUploadFailed(runId, attemptedAtUtc, ex.Message);
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string? ResolvePlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "RunBundleUpload",
                $"ResolvePlayerAccountId failed: {ex.GetType().Name}: {ex.Message}"
            );
            return null;
        }
    }
}
