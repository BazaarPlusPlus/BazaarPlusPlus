#nullable enable
using System.Security.Cryptography;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class BundleUploadFeed : IUploadFeed
{
    internal const int MaximumAttemptBatch = 3;

    public UploadFeedKind Kind => UploadFeedKind.Bundle;

    public IUploadFeedSession Activate(
        IBppServices services,
        UploadFeedLogState logState,
        UploadPumpCadence cadence
    ) => new Session(services, cadence);

    internal sealed class Session : IUploadFeedSession
    {
        private static readonly TimeSpan PendingRetention = TimeSpan.FromDays(14);
        private static readonly TimeSpan PermanentFileRetention = TimeSpan.FromDays(7);
        private const long SoftLimitBytes = 512L * 1024 * 1024;
        private readonly IBppServices _services;
        private readonly ModApiSession _modApiSession;
        private readonly BundleQueueStore _queueStore;
        private readonly IBundleOutboxFiles _files;
        private readonly int _retryIntervalSeconds;

        internal Session(IBppServices services, UploadPumpCadence cadence)
        {
            _services = services;
            var root = services.Paths.RequireDataRoot();
            _queueStore = new BundleQueueStore(PathConstants.RunLogDatabase(root));
            _files = new SystemBundleOutboxFiles(PathConstants.BundleOutbox(root));
            _modApiSession =
                ModApiSession.TryCreate(
                    ModApiUploadDefaults.ApiBaseUrl,
                    BppPluginVersion.Current,
                    "BundleUpload",
                    TimeSpan.FromSeconds(ModApiUploadDefaults.RequestTimeoutSeconds)
                ) ?? throw new InvalidOperationException("V5 mod API route is invalid.");
            _retryIntervalSeconds = cadence.RetryIntervalSeconds;
        }

        internal Session(
            IBppServices services,
            int retryIntervalSeconds,
            ModApiSession modApiSession,
            BundleQueueStore queueStore,
            IBundleOutboxFiles files
        )
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _retryIntervalSeconds = retryIntervalSeconds;
            _modApiSession =
                modApiSession ?? throw new ArgumentNullException(nameof(modApiSession));
            _queueStore = queueStore ?? throw new ArgumentNullException(nameof(queueStore));
            _files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public bool IsEnabled => true;

        public async Task<UploadAttemptResult> RunAttemptAsync(CancellationToken cancellationToken)
        {
            Cleanup();
            var rows = _queueStore.ListDue(DateTimeOffset.UtcNow, MaximumAttemptBatch);
            if (rows.Count == 0)
                return UploadAttemptResult.NoWork();
            var observations = new List<UploadAttemptObservation>();
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var stream = _files.OpenRead(row.FileName);
                    if (stream.Length != row.TotalBytes)
                        throw new InvalidDataException("bundle_length_mismatch");
                    string digest;
                    using (var sha256 = SHA256.Create())
                        digest = BitConverter
                            .ToString(sha256.ComputeHash(stream))
                            .Replace("-", string.Empty)
                            .ToLowerInvariant();
                    if (!string.Equals(digest, row.ContentSha256Hex, StringComparison.Ordinal))
                        throw new InvalidDataException("bundle_digest_mismatch");
                    stream.Seek(0, SeekOrigin.Begin);
                    var response = await _modApiSession
                        .UploadBundleAsync(
                            stream,
                            row.TotalBytes,
                            row.ContentDigest,
                            row.BundleId,
                            row.RunId,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    ApplyResponse(row, response);
                    if (
                        response.Disposition
                        is BundleUploadDisposition.Uploaded
                            or BundleUploadDisposition.Equivalent
                    )
                    {
                        _files.Delete(row.FileName);
                        BundlePipelineLog.Info(
                            BundlePipelineLogEvents.UploadSucceeded,
                            row.RunId,
                            row.BundleId
                        );
                        observations.Add(UploadAttemptObservation.Succeeded(row.RunId));
                    }
                    else if (response.Disposition == BundleUploadDisposition.Transient)
                    {
                        BundlePipelineLog.Warn(
                            BundlePipelineLogEvents.UploadDegraded,
                            response.Code,
                            response.DiagnosticException,
                            runId: row.RunId
                        );
                        observations.Add(
                            UploadAttemptObservation.Degraded(
                                row.RunId,
                                UploadLogReasonCode.RemoteUploadFailed,
                                response.DiagnosticException
                            )
                        );
                        _services.EventBus.Publish(new UploadArmRequested());
                    }
                    else
                    {
                        BundlePipelineLog.Warn(
                            BundlePipelineLogEvents.UploadDegraded,
                            response.Code,
                            response.DiagnosticException,
                            runId: row.RunId
                        );
                        observations.Add(
                            UploadAttemptObservation.Degraded(
                                row.RunId,
                                UploadLogReasonCode.PayloadInvalid,
                                response.DiagnosticException
                            )
                        );
                    }
                }
                catch (FileNotFoundException)
                {
                    _queueStore.FailOutboxAndScheduleReseal(
                        row.BundleId,
                        row.RunId,
                        "pending_file_missing",
                        DateTimeOffset.UtcNow
                    );
                    BundlePipelineLog.Warn(
                        BundlePipelineLogEvents.UploadDegraded,
                        "pending_file_missing",
                        runId: row.RunId
                    );
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            row.RunId,
                            UploadLogReasonCode.PayloadUnreadable
                        )
                    );
                }
                catch (InvalidDataException ex)
                {
                    _queueStore.FailOutboxAndScheduleReseal(
                        row.BundleId,
                        row.RunId,
                        ex.Message,
                        DateTimeOffset.UtcNow
                    );
                    BundlePipelineLog.Warn(
                        BundlePipelineLogEvents.UploadDegraded,
                        ex.Message,
                        ex,
                        row.RunId
                    );
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            row.RunId,
                            UploadLogReasonCode.PayloadInvalid,
                            ex
                        )
                    );
                }
                catch (IOException ex)
                {
                    RecordTransient(row, "file_io_error", ex.Message, null);
                    BundlePipelineLog.Warn(
                        BundlePipelineLogEvents.UploadDegraded,
                        "file_io_error",
                        ex,
                        row.RunId
                    );
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            row.RunId,
                            UploadLogReasonCode.PayloadUnreadable,
                            ex
                        )
                    );
                }
            }
            return UploadAttemptResult.From(observations);
        }

        public IDisposable? SubscribeArmSignals(Action arm) => null;

        public void Dispose() => _modApiSession.Dispose();

        private void ApplyResponse(BundleOutboxRecord row, BundleUploadResponse response)
        {
            if (response.Disposition == BundleUploadDisposition.Transient)
            {
                RecordTransient(row, response.Code, response.Detail, response.RetryAfterSeconds);
                return;
            }
            var uploaded =
                response.Disposition
                is BundleUploadDisposition.Uploaded
                    or BundleUploadDisposition.Equivalent;
            _queueStore.RecordOutcome(
                row.BundleId,
                new BundleUploadOutcomeRecord(
                    uploaded,
                    response.Code,
                    response.Detail,
                    response.RequestId,
                    response.Outcome
                ),
                DateTimeOffset.UtcNow
            );
        }

        private void RecordTransient(
            BundleOutboxRecord row,
            string code,
            string? detail,
            int? retryAfterSeconds
        )
        {
            var delay = Math.Max(_retryIntervalSeconds, retryAfterSeconds ?? 0);
            var now = DateTimeOffset.UtcNow;
            _queueStore.RecordTransient(row.BundleId, code, detail, now, now.AddSeconds(delay));
        }

        private void Cleanup()
        {
            var now = DateTimeOffset.UtcNow;
            _queueStore.ExpirePending(now.Subtract(PendingRetention), now);
            foreach (
                var fileName in _queueStore.ListRetentionFileNames(
                    now.Subtract(PermanentFileRetention)
                )
            )
            {
                if (_files.Exists(fileName))
                    _files.Delete(fileName);
            }
            EnforceSoftLimit(now);
        }

        private void EnforceSoftLimit(DateTimeOffset now)
        {
            var total = _files
                .EnumerateBundleFileNames()
                .Where(_files.Exists)
                .Sum(_files.GetLength);
            if (total <= SoftLimitBytes)
                return;
            foreach (
                var fileName in _queueStore.ListReclaimFileNames(now.Subtract(PendingRetention))
            )
            {
                if (total <= SoftLimitBytes)
                    break;
                if (!_files.Exists(fileName))
                    continue;
                var length = _files.GetLength(fileName);
                _files.Delete(fileName);
                total -= length;
            }
        }
    }
}

internal sealed class UploadPumpMount : IBppMountable
{
    private BackgroundUploadPump? _pump;

    public void Mount(UnityEngine.GameObject host, IBppServices services)
    {
        _pump = host.AddComponent<BackgroundUploadPump>();
        _pump.Initialize(services, new BundleUploadFeed());
    }

    public void Unmount(UnityEngine.GameObject host)
    {
        if (_pump == null)
            return;
        UnityEngine.Object.DestroyImmediate(_pump);
        _pump = null;
    }
}
