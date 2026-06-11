#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadService
{
    private const int BatchSize = 3;

    private readonly BazaarDbSnapshotUploadStore _store;
    private readonly ModApiRoutes _routes;
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _playerAccountIdResolver;

    public BazaarDbSnapshotUploadService(
        BazaarDbSnapshotUploadStore store,
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

    public Task UploadPendingAsync(CancellationToken cancellationToken) =>
        UploadPendingAsync(
            _playerAccountIdResolver()?.Trim(),
            TryResolvePlayerName(),
            cancellationToken
        );

    public Task UploadPendingInBackgroundAsync(CancellationToken cancellationToken)
    {
        var playerAccountId = _playerAccountIdResolver()?.Trim();
        var playerName = TryResolvePlayerName();
        return Task.Run(
            () => UploadPendingAsync(playerAccountId, playerName, cancellationToken),
            cancellationToken
        );
    }

    private async Task UploadPendingAsync(
        string? playerAccountId,
        string? playerName,
        CancellationToken cancellationToken
    )
    {
        _store.EnsureBackfilled();

        var pending = _store.GetPendingSnapshotIds(BatchSize);
        if (pending.Count == 0)
        {
            BppLog.Info(
                "BazaarDbSnapshotUploadService",
                "No screenshots are waiting for BazaarDB upload."
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(playerAccountId))
        {
            BppLog.Info(
                "BazaarDbSnapshotUploadService",
                $"Skipping {pending.Count} pending screenshot(s): player account id not yet available."
            );
            return;
        }

        var healthProbe = await new ModApiHealthClient(_httpClient, _routes)
            .ProbeAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!healthProbe.Succeeded)
        {
            BppLog.Warn(
                "BazaarDbSnapshotUploadService",
                $"Bazaar++ service health probe failed error={healthProbe.Error ?? "unknown"} rtt_ms={healthProbe.RoundTripMilliseconds} probed_at_utc={healthProbe.ProbedAtUtc:O}; retrying later."
            );
            return;
        }

        var serverTimeUtc = healthProbe.ServerTimeUtc?.ToString("O") ?? "unknown";
        BppLog.Debug(
            "BazaarDbSnapshotUploadService",
            $"Bazaar++ service health ok rtt_ms={healthProbe.RoundTripMilliseconds} server_time_utc={serverTimeUtc} probed_at_utc={healthProbe.ProbedAtUtc:O}."
        );

        var client = new BazaarDbSnapshotClient(_httpClient, _routes);
        foreach (var snapshotId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAtUtc = DateTime.UtcNow;
            try
            {
                var snapshot = _store.TryBuildSnapshot(
                    snapshotId,
                    playerAccountId,
                    playerName,
                    cancellationToken
                );
                if (snapshot == null)
                {
                    var buildFailureReason =
                        _store.LastBuildFailureReason ?? "build_snapshot_failed";
                    if (IsTransientBuildFailure(buildFailureReason))
                        _store.MarkTransientFailure(snapshotId, attemptedAtUtc, buildFailureReason);
                    else
                        _store.MarkPermanentFailure(snapshotId, attemptedAtUtc, buildFailureReason);
                    continue;
                }

                var result = await client.UploadSnapshotAsync(snapshot.Payload, cancellationToken);
                if (result.Succeeded)
                {
                    _store.MarkUploaded(snapshotId, DateTime.UtcNow);
                    continue;
                }

                var error = result.Error ?? "bazaardb_upload_failed";
                if (result.Permanent)
                    _store.MarkPermanentFailure(snapshotId, attemptedAtUtc, error);
                else
                    _store.MarkTransientFailure(snapshotId, attemptedAtUtc, error);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _store.MarkTransientFailure(snapshotId, attemptedAtUtc, ex.Message);
            }
        }
    }

    private static bool IsTransientBuildFailure(string reason)
    {
        return string.Equals(reason, "image_prepare_timeout", StringComparison.Ordinal);
    }

    private static string? TryResolvePlayerName()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileDisplayUsername()
                ?? BppClientCacheBridge.TryGetProfileUsername();
        }
        catch
        {
            return null;
        }
    }
}
