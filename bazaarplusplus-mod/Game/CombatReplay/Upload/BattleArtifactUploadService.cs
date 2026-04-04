#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.ModApi;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleArtifactUploadService : IDisposable
{
    private readonly BattleUploadSqliteStore _store;
    private readonly ModApiIdentityStore _identityStore;
    private readonly ModApiClientStateStore _clientStateStore;
    private readonly ModApiKeyStore _keyStore;
    private readonly ModApiRoutes _routes;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    public BattleArtifactUploadService(
        BattleUploadSqliteStore store,
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

    public async Task<BattleArtifactUploadCycleResult> UploadPendingBattleArtifactsAsync(
        CancellationToken cancellationToken
    )
    {
        var pendingBattleIds = _store.GetPendingBattleIds(_batchSize);
        if (pendingBattleIds.Count == 0)
        {
            BppLog.Info(
                "BattleArtifactUploadService",
                "No battle artifacts are waiting for upload."
            );
            return new BattleArtifactUploadCycleResult(uploadedCount: 0, hasMorePending: false);
        }

        BppLog.Info(
            "BattleArtifactUploadService",
            $"Starting upload cycle for {pendingBattleIds.Count} pending battle artifact(s)."
        );

        var installId = _identityStore.GetOrCreateInstallId();
        var uploadedCount = 0;
        var apiClient = new BattleArtifactUploadApiClient(
            _httpClient,
            new BattleArtifactUploadRequestSigner(_keyStore),
            _routes.UploadBattleArtifact
        );
        var routeClient = CreateAuthenticatedRouteClient();

        foreach (var battleId in pendingBattleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            BppLog.Info("BattleArtifactUploadService", $"Preparing upload for battle {battleId}.");
            var preflightSnapshot = _store.TryBuildBattleArtifactSnapshot(
                battleId,
                installId,
                clientId: null
            );
            if (preflightSnapshot == null)
            {
                _store.MarkReplayUploadTerminalFailure(
                    battleId,
                    attemptedAtUtc,
                    "replay_snapshot_not_found"
                );
                BppLog.Warn(
                    "BattleArtifactUploadService",
                    $"Marking battle {battleId} as terminal failure because the local snapshot is unavailable."
                );
                continue;
            }

            BattleArtifactUploadSnapshot? snapshot = null;
            try
            {
                var requestResult = await routeClient.SendAsync(
                    installId,
                    async (clientId, token) =>
                    {
                        snapshot = _store.TryBuildBattleArtifactSnapshot(
                            battleId,
                            installId,
                            clientId
                        );
                        if (snapshot == null)
                        {
                            return BattleArtifactUploadApiResult.Failure(
                                "replay_snapshot_not_found",
                                shouldFallback: false,
                                shouldReRegister: false
                            );
                        }

                        BppLog.Info(
                            "BattleArtifactUploadService",
                            $"Uploading battle {battleId}."
                        );
                        return await apiClient.UploadBattleAsync(
                            snapshot.Json,
                            clientId,
                            installId,
                            battleId,
                            snapshot.Payload.RunId,
                            token
                        );
                    },
                    cancellationToken
                );

                if (!requestResult.RegistrationAvailable)
                {
                    _store.MarkReplayUploadFailed(
                        battleId,
                        attemptedAtUtc,
                        "registration_unavailable"
                    );
                    BppLog.Warn(
                        "BattleArtifactUploadService",
                        $"Skipping battle {battleId} because client registration is unavailable."
                    );
                    continue;
                }

                var uploadResult = requestResult.Response;
                if (!uploadResult.Succeeded)
                {
                    if (
                        string.Equals(
                            uploadResult.Error,
                            "replay_snapshot_not_found",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        _store.MarkReplayUploadTerminalFailure(
                            battleId,
                            attemptedAtUtc,
                            "replay_snapshot_not_found"
                        );
                        BppLog.Warn(
                            "BattleArtifactUploadService",
                            $"Marking battle {battleId} as terminal failure because the local snapshot disappeared before upload."
                        );
                        continue;
                    }

                    _store.MarkReplayUploadFailed(
                        battleId,
                        attemptedAtUtc,
                        uploadResult.Error ?? "upload_failed"
                    );
                    BppLog.Warn(
                        "BattleArtifactUploadService",
                        $"Upload failed for battle {battleId}: {uploadResult.Error ?? "unknown_error"}."
                    );
                    continue;
                }

                if (snapshot == null)
                {
                    _store.MarkReplayUploadTerminalFailure(
                        battleId,
                        attemptedAtUtc,
                        "replay_snapshot_not_found"
                    );
                    BppLog.Warn(
                        "BattleArtifactUploadService",
                        $"Marking battle {battleId} as terminal failure because the local snapshot was lost before completion."
                    );
                    continue;
                }

                _store.MarkReplayUploaded(battleId, DateTimeOffset.UtcNow);
                BppLog.Info(
                    "BattleArtifactUploadService",
                    $"Uploaded battle {battleId} with object_key={uploadResult.ObjectKey ?? "none"}."
                );
                uploadedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkReplayUploadFailed(
                    battleId,
                    attemptedAtUtc,
                    ModApiErrorFormatter.Truncate(ex.Message)
                );
                BppLog.Warn(
                    "BattleArtifactUploadService",
                    $"Upload failed for battle {battleId}: {ex.GetType().Name} - {ex.Message}"
                );
            }
        }

        var hasMorePending = _store.HasMorePendingReplays();
        BppLog.Info(
            "BattleArtifactUploadService",
            $"Battle upload cycle finished: uploaded={uploadedCount}, remaining={(hasMorePending ? "yes" : "no")}."
        );
        return new BattleArtifactUploadCycleResult(uploadedCount, hasMorePending);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
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
