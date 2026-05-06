#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadService : IDisposable
{
    private const string AnonymousPlayerAccountId = "anonymous-player";

    private readonly RunBundleUploadStore _store;
    private readonly V3Routes _routes;
    private readonly HttpClient _httpClient;

    public RunBundleUploadService(RunBundleUploadStore store, V3Routes routes, TimeSpan timeout)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<RunBundleUploadCycleResult> UploadPendingRunBundlesAsync(
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
            return new RunBundleUploadCycleResult(uploadedCount: 0, hasMorePending: false);
        }

        var playerAccountId = ResolvePlayerAccountId() ?? AnonymousPlayerAccountId;

        var uploadedCount = 0;
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

                var result = await client.UploadRunBundleAsync(snapshot.Payload, cancellationToken);
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
                uploadedCount++;
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

        return new RunBundleUploadCycleResult(uploadedCount, _store.HasMorePendingCompletedRuns());
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
        catch
        {
            return null;
        }
    }
}
