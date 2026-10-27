#nullable enable
using System.Security.Cryptography;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.BundlePipeline;

public static class BundleUploadFeed
{
    public const int MaximumAttemptBatch = 3;

    /// <summary>The cap on every upload retry delay, Retry-After included.</summary>
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(30);

    // A queue backoff window counts as over within this fraction of the base interval of its end;
    // larger than the first step's largest jitter (a tenth), so a jittered first retry still
    // lands on the pump tick one interval after the failed attempt started.
    private const double WindowEndSlackFraction = 0.15;

    public static Session Activate(
        IBppServices services,
        UploadPumpCadence cadence,
        string? platformKey,
        SqliteOpenOptions databaseOptions
    ) => new(services, cadence, platformKey, databaseOptions);

    /// <summary>
    /// The retry delay after a failure: <paramref name="baseSeconds"/> doubled per prior
    /// consecutive failure, scaled by a jitter factor in [0.9, 1.1] drawn from
    /// <paramref name="jitterSample"/> (a caller-supplied uniform sample in [0, 1)), floored by
    /// the server's Retry-After, and capped at <see cref="MaximumRetryDelay"/>.
    /// </summary>
    public static TimeSpan RetryDelay(
        int baseSeconds,
        int priorFailures,
        double jitterSample,
        int? retryAfterSeconds
    )
    {
        var exponent = Math.Min(Math.Max(priorFailures, 0), 30);
        var sample = double.IsNaN(jitterSample) ? 0.5d : Math.Min(Math.Max(jitterSample, 0d), 1d);
        var factor = 1d + 0.2d * (sample - 0.5d);
        var backoff = Math.Max(baseSeconds, 0) * Math.Pow(2, exponent) * factor;
        var seconds = Math.Max(backoff, Math.Max(retryAfterSeconds ?? 0, 0));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds));
    }

    /// <summary>
    /// Feed-owned behavior for one pump lifetime: enablement, one attempt, the in-memory queue
    /// backoff and the arm signals that end it, and attempt-resource disposal. The pump owns
    /// Unity cadence, its own arm of the next attempt, and shutdown drain.
    /// </summary>
    public sealed class Session : IDisposable
    {
        private static readonly TimeSpan PendingRetention = TimeSpan.FromDays(14);
        private static readonly TimeSpan PermanentFileRetention = TimeSpan.FromDays(7);
        private const long SoftLimitBytes = 512L * 1024 * 1024;
        private readonly IBppServices _services;
        private readonly ModApiSession _modApiSession;
        private readonly BundleQueueStore _queueStore;
        private readonly IBundleOutboxFiles _files;
        private readonly int _retryIntervalSeconds;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<double> _jitterSample;

        // Queue backoff after EndpointUnavailable outcomes. Only the single in-flight attempt
        // touches these two; another thread only sets _backoffClearRequested.
        private int _consecutiveEndpointFailures;
        private DateTimeOffset? _queueBackoffUntil;
        private int _backoffClearRequested;

        public Session(
            IBppServices services,
            UploadPumpCadence cadence,
            string? platformKey,
            SqliteOpenOptions databaseOptions
        )
        {
            _services = services;
            var root = services.Paths.RequireDataRoot();
            _queueStore = new BundleQueueStore(PathConstants.RunLogDatabase(root), databaseOptions);
            _files = new SystemBundleOutboxFiles(PathConstants.BundleOutbox(root));
            _modApiSession =
                ModApiSession.TryCreate(
                    ModApiUploadDefaults.ApiBaseUrl,
                    BppPluginVersion.Current,
                    platformKey,
                    "BundleUpload",
                    TimeSpan.FromSeconds(ModApiUploadDefaults.RequestTimeoutSeconds)
                ) ?? throw new InvalidOperationException("Mod API route is invalid.");
            _retryIntervalSeconds = cadence.RetryIntervalSeconds;
            _utcNow = static () => DateTimeOffset.UtcNow;
            _jitterSample = new Random().NextDouble;
        }

        public Session(
            IBppServices services,
            int retryIntervalSeconds,
            ModApiSession modApiSession,
            BundleQueueStore queueStore,
            IBundleOutboxFiles files,
            Func<DateTimeOffset>? utcNow = null,
            Func<double>? jitterSample = null
        )
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _retryIntervalSeconds = retryIntervalSeconds;
            _modApiSession =
                modApiSession ?? throw new ArgumentNullException(nameof(modApiSession));
            _queueStore = queueStore ?? throw new ArgumentNullException(nameof(queueStore));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
            _jitterSample = jitterSample ?? new Random().NextDouble;
        }

        public async Task<UploadAttemptResult> RunAttemptAsync(CancellationToken cancellationToken)
        {
            // One instant for the whole attempt: the backoff window opens from it, so the pump's
            // next tick at attempt start + interval lands on the window edge, not past it.
            var startedAt = _utcNow();
            if (Interlocked.Exchange(ref _backoffClearRequested, 0) == 1)
                EndQueueBackoff();
            Cleanup(startedAt);
            if (IsQueueBackingOff(startedAt))
                return UploadAttemptResult.From(
                    UploadAttemptObservation.Deferred(UploadLogReasonCode.QueueBackoff)
                );
            var rows = _queueStore.ListDue(startedAt, MaximumAttemptBatch);
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
                    switch (response.Disposition)
                    {
                        case BundleUploadDisposition.Uploaded:
                        case BundleUploadDisposition.Equivalent:
                            // The upload is committed; a file delete that throws below must not
                            // keep the queue backing off.
                            EndQueueBackoff();
                            _files.Delete(row.FileName);
                            BundlePipelineLog.Info(
                                new BppLogEvent(
                                    BppLogFeatureScope.BundlePipeline,
                                    "bundle_pipeline.upload.succeeded"
                                ),
                                row.RunId,
                                row.BundleId
                            );
                            observations.Add(UploadAttemptObservation.Succeeded(row.RunId));
                            break;
                        case BundleUploadDisposition.Transient:
                            LogUploadDegraded(row, response);
                            observations.Add(
                                UploadAttemptObservation.Degraded(
                                    row.RunId,
                                    UploadLogReasonCode.RemoteUploadFailed,
                                    response.DiagnosticException
                                )
                            );
                            break;
                        case BundleUploadDisposition.EndpointUnavailable:
                            LogUploadDegraded(row, response);
                            observations.Add(
                                UploadAttemptObservation.Degraded(
                                    row.RunId,
                                    UploadLogReasonCode.RemoteUploadFailed,
                                    response.DiagnosticException
                                )
                            );
                            // The rest of the batch would fail the same way: stop here and back
                            // the whole queue off instead of reading, hashing and sending each.
                            OpenQueueBackoff(startedAt, response);
                            return UploadAttemptResult.From(observations);
                        case BundleUploadDisposition.Permanent:
                            LogUploadDegraded(row, response);
                            observations.Add(
                                UploadAttemptObservation.Degraded(
                                    row.RunId,
                                    UploadLogReasonCode.PayloadInvalid,
                                    response.DiagnosticException
                                )
                            );
                            break;
                        default:
                            throw UnhandledDisposition(response.Disposition);
                    }
                }
                catch (FileNotFoundException)
                {
                    ScheduleReseal(row, "pending_file_missing");
                    BundlePipelineLog.Warn(
                        new BppLogEvent(
                            BppLogFeatureScope.BundlePipeline,
                            "bundle_pipeline.upload.degraded",
                            storm: ["category"]
                        ),
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
                    ScheduleReseal(row, ex.Message);
                    BundlePipelineLog.Warn(
                        new BppLogEvent(
                            BppLogFeatureScope.BundlePipeline,
                            "bundle_pipeline.upload.degraded",
                            storm: ["category"]
                        ),
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
                    RecordTransient(row, "file_io_error", ex.Message, RowRetryDelay(row, null));
                    BundlePipelineLog.Warn(
                        new BppLogEvent(
                            BppLogFeatureScope.BundlePipeline,
                            "bundle_pipeline.upload.degraded",
                            storm: ["category"]
                        ),
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

        private void ScheduleReseal(BundleOutboxRecord row, string reason)
        {
            if (_queueStore.FailOutboxAndScheduleReseal(row.BundleId, row.RunId, reason, _utcNow()))
                _services.EventBus.Publish(new BundleSealJobRecreated { RunId = row.RunId });
        }

        /// <summary>
        /// Handles the shared arm signals for the queue backoff: an explicit arm
        /// (<see cref="UploadArmRequested"/>, or leaving a run) ends it, so the attempt the pump
        /// arms really runs. The pump subscribes this before its own arm handlers, so the bus,
        /// which calls handlers in subscription order, clears the backoff before the pump can
        /// start that attempt. The pump holds the handle and disposes it first on shutdown;
        /// Dispose only owns attempt resources.
        /// </summary>
        public IDisposable SubscribeArmSignals() =>
            new ArmSubscriptions(
                _services.EventBus.Subscribe<UploadArmRequested>(_ => RequestBackoffClear()),
                _services.EventBus.Subscribe<RunLifecycleChanged>(change =>
                {
                    if (change != null && !change.IsInGameRun)
                        RequestBackoffClear();
                })
            );

        public void Dispose() => _modApiSession.Dispose();

        // Set from the event bus on any thread; the next attempt consumes it at its start, so an
        // arm that lands during an in-flight attempt is not undone by that attempt's failure.
        private void RequestBackoffClear() => Interlocked.Exchange(ref _backoffClearRequested, 1);

        private void EndQueueBackoff()
        {
            _consecutiveEndpointFailures = 0;
            _queueBackoffUntil = null;
        }

        // The window is measured from the attempt's start, so it lines up with the pump tick, but
        // it never ends sooner after the failure itself than the base interval (or the server's
        // Retry-After): an attempt that spent its window on a slow upload and a request timeout
        // must not retry straight away.
        private void OpenQueueBackoff(DateTimeOffset startedAt, BundleUploadResponse response)
        {
            var delay = RetryDelay(
                _retryIntervalSeconds,
                _consecutiveEndpointFailures,
                _jitterSample(),
                response.RetryAfterSeconds
            );
            var minimumQuiet = RetryDelay(
                _retryIntervalSeconds,
                0,
                0.5d,
                response.RetryAfterSeconds
            );
            var fromStart = startedAt + delay;
            var fromFailure = _utcNow() + minimumQuiet;
            _consecutiveEndpointFailures++;
            _queueBackoffUntil = fromStart > fromFailure ? fromStart : fromFailure;
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.upload.backoff_opened"
                ),
                ("category", response.Code),
                ("consecutive_failures", _consecutiveEndpointFailures),
                ("delay_seconds", (long)Math.Round(delay.TotalSeconds))
            );
        }

        // A window counts as over within a fraction of the base interval of its end, so a pump tick
        // that lands a little early still attempts. A window longer than the cap means the wall
        // clock moved backwards; that ends it instead of stretching it. Only success, an arm, or
        // a new Session resets the failure count.
        private bool IsQueueBackingOff(DateTimeOffset now)
        {
            if (_queueBackoffUntil is not { } until)
                return false;
            var remaining = until - now;
            if (
                remaining.TotalSeconds >= _retryIntervalSeconds * WindowEndSlackFraction
                && remaining <= MaximumRetryDelay
            )
                return true;
            _queueBackoffUntil = null;
            return false;
        }

        private static void LogUploadDegraded(
            BundleOutboxRecord row,
            BundleUploadResponse response
        ) =>
            BundlePipelineLog.Warn(
                new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.upload.degraded",
                    storm: ["category"]
                ),
                response.Code,
                response.DiagnosticException,
                runId: row.RunId
            );

        private static InvalidOperationException UnhandledDisposition(
            BundleUploadDisposition disposition
        ) => new($"Unhandled Bundle upload disposition '{disposition}'.");

        private void ApplyResponse(BundleOutboxRecord row, BundleUploadResponse response)
        {
            switch (response.Disposition)
            {
                case BundleUploadDisposition.Uploaded:
                case BundleUploadDisposition.Equivalent:
                case BundleUploadDisposition.Permanent:
                    _queueStore.RecordOutcome(
                        row.BundleId,
                        new BundleUploadOutcomeRecord(
                            response.Disposition != BundleUploadDisposition.Permanent,
                            response.Code,
                            response.Detail,
                            response.RequestId,
                            response.Outcome
                        ),
                        _utcNow()
                    );
                    return;
                case BundleUploadDisposition.Transient:
                    RecordTransient(
                        row,
                        response.Code,
                        response.Detail,
                        RowRetryDelay(row, response.RetryAfterSeconds)
                    );
                    return;
                case BundleUploadDisposition.EndpointUnavailable:
                    // The queue backoff owns the wait; the row keeps only the server's own
                    // Retry-After, so a restart or an arm retries it at once.
                    RecordTransient(
                        row,
                        response.Code,
                        response.Detail,
                        RetryDelay(0, 0, 0d, response.RetryAfterSeconds)
                    );
                    return;
                default:
                    throw UnhandledDisposition(response.Disposition);
            }
        }

        // A failure about this Bundle backs off this row only, from its recorded attempt count.
        private TimeSpan RowRetryDelay(BundleOutboxRecord row, int? retryAfterSeconds) =>
            RetryDelay(_retryIntervalSeconds, row.Attempts, _jitterSample(), retryAfterSeconds);

        private void RecordTransient(
            BundleOutboxRecord row,
            string code,
            string? detail,
            TimeSpan delay
        )
        {
            var now = _utcNow();
            _queueStore.RecordTransient(row.BundleId, code, detail, now, now + delay);
        }

        private void Cleanup(DateTimeOffset now)
        {
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

        private sealed class ArmSubscriptions(IDisposable first, IDisposable second) : IDisposable
        {
            public void Dispose()
            {
                first.Dispose();
                second.Dispose();
            }
        }
    }
}
