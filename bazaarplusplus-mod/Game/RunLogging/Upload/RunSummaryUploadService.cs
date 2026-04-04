#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.ModApi;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunSummaryUploadService : IDisposable
{
    private readonly RunUploadSqliteStore _store;
    private readonly ModApiIdentityStore _identityStore;
    private readonly ModApiClientStateStore _clientStateStore;
    private readonly ModApiKeyStore _keyStore;
    private readonly ModApiRoutes _routes;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    public RunSummaryUploadService(
        RunUploadSqliteStore store,
        ModApiIdentityStore identityStore,
        ModApiClientStateStore clientStateStore,
        ModApiKeyStore keyStore,
        ModApiRoutes routes,
        int batchSize,
        TimeSpan timeout
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));

        _batchSize = Math.Max(1, batchSize);
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<RunSummaryUploadCycleResult> UploadPendingRunSummariesAsync(
        CancellationToken cancellationToken
    )
    {
        var pendingRunIds = _store.GetPendingCompletedRunIds(_batchSize);
        if (pendingRunIds.Count == 0)
        {
            BppLog.Info("RunSummaryUploadService", "No completed runs are waiting for upload.");
            return new RunSummaryUploadCycleResult(uploadedCount: 0, hasMorePending: false);
        }

        BppLog.Info(
            "RunSummaryUploadService",
            $"Starting upload cycle for {pendingRunIds.Count} pending run(s)."
        );

        var installId = _identityStore.GetOrCreateInstallId();
        var uploadedCount = 0;
        var apiClient = CreateApiClient();
        var routeClient = CreateAuthenticatedRouteClient();
        foreach (var runId in pendingRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            RunSummaryUploadSnapshot? snapshot = null;
            BppLog.Info("RunSummaryUploadService", $"Preparing upload for run {runId}.");
            try
            {
                var requestResult = await routeClient.SendAsync(
                    installId,
                    async (clientId, token) =>
                    {
                        snapshot = _store.TryBuildRunSummarySnapshot(runId, installId, clientId);
                        if (snapshot == null)
                        {
                            return RunSummaryUploadApiResult.Failure(
                                "run_snapshot_not_found",
                                shouldFallback: false,
                                shouldReRegister: false
                            );
                        }

                        BppLog.Info(
                            "RunSummaryUploadService",
                            $"Uploading run {runId}."
                        );
                        var json = JsonConvert.SerializeObject(
                            snapshot.Payload,
                            ModApiSerialization.SerializerSettings
                        );
                        return await apiClient.UploadRunAsync(
                            json,
                            clientId,
                            installId,
                            runId,
                            token
                        );
                    },
                    cancellationToken
                );

                if (!requestResult.RegistrationAvailable)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "registration_unavailable");
                    BppLog.Warn(
                        "RunSummaryUploadService",
                        $"Skipping run {runId} because client registration is unavailable."
                    );
                    continue;
                }

                var uploadResult = requestResult.Response;
                if (!uploadResult.Succeeded)
                {
                    _store.MarkRunUploadFailed(
                        runId,
                        attemptedAtUtc,
                        uploadResult.Error ?? "upload_failed"
                    );
                    BppLog.Warn(
                        "RunSummaryUploadService",
                        $"Upload failed for run {runId}: {uploadResult.Error ?? "unknown_error"}."
                    );
                    continue;
                }

                if (snapshot == null)
                {
                    _store.MarkRunUploadFailed(runId, attemptedAtUtc, "run_snapshot_not_found");
                    continue;
                }

                _store.MarkRunUploaded(
                    runId,
                    snapshot.LastSeq,
                    snapshot.UploadedStatus,
                    DateTimeOffset.UtcNow
                );
                BppLog.Info(
                    "RunSummaryUploadService",
                    $"Uploaded run {runId} with last_seq={snapshot.LastSeq}, status={snapshot.UploadedStatus ?? "unknown"}."
                );
                uploadedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkRunUploadFailed(
                    runId,
                    attemptedAtUtc,
                    ModApiErrorFormatter.Truncate(ex.Message)
                );
                BppLog.Warn(
                    "RunSummaryUploadService",
                    $"Upload failed for run {runId}: {ex.GetType().Name} - {ex.Message}"
                );
            }
        }

        var hasMorePending = _store.HasMorePendingCompletedRuns();
        BppLog.Info(
            "RunSummaryUploadService",
            $"Run upload cycle finished: uploaded={uploadedCount}, remaining={(hasMorePending ? "yes" : "no")}."
        );
        return new RunSummaryUploadCycleResult(uploadedCount, hasMorePending);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private RunSummaryUploadApiClient CreateApiClient()
    {
        return new RunSummaryUploadApiClient(
            _httpClient,
            new ModApiRequestSigner(_keyStore),
            _routes.UploadRunSummary
        );
    }

    private ModApiAuthenticatedSession CreateAuthenticatedRouteClient()
    {
        var registrationClient = new ModApiRegistrationClient(
            _httpClient,
            _clientStateStore,
            _keyStore,
            _routes.RegisterClient
        );
        return new ModApiAuthenticatedSession(registrationClient, _clientStateStore);
    }
}
