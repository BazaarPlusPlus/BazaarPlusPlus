#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class BundleSealCoordinator : IBppFeature, IDisposable
{
    private static readonly TimeSpan InputConvergenceWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FallbackScanInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OrphanFileRetention = TimeSpan.FromHours(24);
    private readonly IBppServices _services;
    private readonly string _databasePath;
    private readonly string _replayRoot;
    private readonly string _screenshotRoot;
    private readonly string _outboxRoot;
    private readonly BundleQueueStore _queueStore;
    private readonly RunScreenshotSqliteStore _screenshotStore;
    private readonly RunPayloadComposer _composer;
    private readonly BundleScreenshotEncoder _screenshotEncoder = new();
    private readonly UlidV5Generator _ulid = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly ConcurrentQueue<ScreenshotCaptureTerminal> _screenshotTerminals = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Action<BundleSealStage, string>? _faultProbe;

    // Environment-blocked jobs become due once per launch (BundleSealFailurePolicy.IsDue).
    private readonly DateTimeOffset _launchedAtUtc;
    private IDisposable? _runInitialized;
    private IDisposable? _runLifecycle;
    private IDisposable? _replayDrained;
    private IDisposable? _screenshotTerminal;
    private Task? _worker;
    private int _wakePending;
    private bool _started;

    internal BundleSealCoordinator(IBppServices services)
        : this(services, faultProbe: null) { }

    // Tests inject failures at named seal stages; production passes null.
    internal BundleSealCoordinator(
        IBppServices services,
        Action<BundleSealStage, string>? faultProbe
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _faultProbe = faultProbe;
        _launchedAtUtc = DateTimeOffset.UtcNow;
        var dataRoot = services.Paths.RequireDataRoot();
        _databasePath = PathConstants.RunLogDatabase(dataRoot);
        _replayRoot = PathConstants.CombatReplays(dataRoot);
        _screenshotRoot = PathConstants.Screenshots(dataRoot);
        _outboxRoot = PathConstants.BundleOutbox(dataRoot);
        _queueStore = new BundleQueueStore(_databasePath);
        _screenshotStore = new RunScreenshotSqliteStore(_databasePath);
        _composer = new RunPayloadComposer(_databasePath, _replayRoot);
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        Directory.CreateDirectory(_outboxRoot);
        _queueStore.ResetInterruptedSeals();
        _runInitialized = _services.EventBus.Subscribe<RunInitializedObserved>(_ => Signal());
        _runLifecycle = _services.EventBus.Subscribe<RunLifecycleChanged>(_ => Signal());
        _replayDrained = _services.EventBus.Subscribe<CombatReplayPersistenceDrained>(_ =>
            Signal()
        );
        _screenshotTerminal = _services.EventBus.Subscribe<ScreenshotCaptureTerminal>(terminal =>
        {
            _screenshotTerminals.Enqueue(terminal);
            Signal();
        });
        _worker = Task.Run(() => WorkerAsync(_shutdown.Token));
        Signal();
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (!_started)
            return;
        _started = false;
        _runInitialized?.Dispose();
        _runLifecycle?.Dispose();
        _replayDrained?.Dispose();
        _screenshotTerminal?.Dispose();
        _shutdown.Cancel();
        _wake.Release();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Shutdown is best effort; jobs are durable and reconcile at next startup.
        }
        _shutdown.Dispose();
        _wake.Dispose();
    }

    internal void Signal()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 0)
            _wake.Release();
    }

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Maintenance failures are logged per step; they never keep waiting jobs from sealing.
        Maintain(BundleSealStage.ScreenshotTerminal, ApplyScreenshotTerminals);
        Maintain(BundleSealStage.FileRecovery, RecoverFiles);
        Maintain(BundleSealStage.JobDiscovery, EnsureSealJobs);
        var published = false;
        try
        {
            foreach (var runId in _queueStore.ListWaitingRunIds())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // One Run's failure is recorded against that Run; it cannot abort the pass.
                try
                {
                    published |= await TrySealAsync(runId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    RecordJobFailure(runId, null, BundleSealStage.Attempt, ex);
                }
            }
        }
        finally
        {
            // A recovered backlog arms one upload batch, not one immediate batch per run.
            if (published)
                _services.EventBus.Publish(new UploadArmRequested());
        }
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _wake
                    .WaitAsync(FallbackScanInterval, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _wakePending, 0);
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                BundlePipelineLog.Warn(
                    new BppLogEvent(
                        BppLogFeatureScope.BundlePipeline,
                        "bundle_pipeline.reconcile.failed",
                        storm: ["category"]
                    ),
                    "reconcile_exception",
                    ex
                );
            }
        }
    }

    private async Task<bool> TrySealAsync(string runId, CancellationToken cancellationToken)
    {
        var job = _queueStore.ReadJob(runId);
        if (job == null || job.State == BundleSealJobState.TerminalFailure)
            return false;
        var now = DateTimeOffset.UtcNow;
        if (
            !BundleSealFailurePolicy.IsDue(
                job.LastErrorCode,
                job.Attempts,
                job.LastAttemptAtUtc is { } lastAttempt
                    ? (float)(now - lastAttempt).TotalSeconds
                    : null,
                (float)(now - _launchedAtUtc).TotalSeconds
            )
        )
            return false;
        var secondsUntilInputDeadline = (float)(job.InputDeadlineAtUtc - now).TotalSeconds;
        var jobFacts = new BundleSealJobFacts(
            job.ScreenshotRequested,
            job.ScreenshotState == BundleScreenshotState.Unavailable
        );
        if (
            BundleSealConvergence.Resolve(
                jobFacts,
                secondsUntilInputDeadline,
                BundleSealInputObservation.Availability(
                    BundleSealInputGate.ReplayPersistence,
                    !ReplayPersistenceStateTracker.HasPending(runId)
                )
            ) == BundleSealConvergenceDecision.Wait
        )
            return false;

        var stage = BundleSealStage.AccountResolution;
        try
        {
            Probe(stage, runId);
            string playerAccountId;
            try
            {
                playerAccountId = _composer.ResolvePlayerAccountId(runId, job.PlayerAccountId);
                _queueStore.FreezePlayerAccountId(runId, playerAccountId);
            }
            catch (BundleCompositionException ex)
            {
                if (ex.Code == "player_account_id_missing")
                {
                    var decision = BundleSealConvergence.Resolve(
                        jobFacts,
                        secondsUntilInputDeadline,
                        BundleSealInputObservation.Availability(
                            BundleSealInputGate.PlayerAccount,
                            false
                        )
                    );
                    if (decision == BundleSealConvergenceDecision.Wait)
                        return false;
                }
                MarkJobTerminal(runId, ex.Code, ex);
                return false;
            }

            stage = BundleSealStage.Screenshot;
            Probe(stage, runId);
            BundleScreenshotBuildInputV5? screenshot = null;
            if (job.ScreenshotRequested && job.ScreenshotState != BundleScreenshotState.Unavailable)
            {
                var source = TryReadScreenshot(runId);
                if (source == null)
                {
                    var screenshotDecision = BundleSealConvergence.Resolve(
                        jobFacts,
                        secondsUntilInputDeadline,
                        BundleSealInputObservation.Availability(
                            BundleSealInputGate.Screenshot,
                            false
                        )
                    );
                    if (screenshotDecision == BundleSealConvergenceDecision.Wait)
                        return false;
                    if (
                        screenshotDecision
                        == BundleSealConvergenceDecision.MarkScreenshotTimedOutAndContinue
                    )
                        _queueStore.UpdateScreenshotState(runId, BundleScreenshotState.TimedOut);
                }
                else
                {
                    screenshot = await _screenshotEncoder
                        .EncodeAsync(source.AbsolutePath, source.CapturedAtMs, cancellationToken)
                        .ConfigureAwait(false);
                    if (screenshot == null)
                        _queueStore.UpdateScreenshotState(runId, BundleScreenshotState.Unavailable);
                    else
                        _queueStore.UpdateScreenshotState(runId, BundleScreenshotState.Available);
                }
            }

            stage = BundleSealStage.Composition;
            Probe(stage, runId);
            RunPayloadComposition composition;
            try
            {
                composition = _composer.Compose(runId, playerAccountId);
            }
            catch (BundleCompositionException ex)
            {
                MarkJobTerminal(runId, ex.Code, ex);
                return false;
            }

            stage = BundleSealStage.ReplayDecode;
            Probe(stage, runId);
            var retry = RetryFacts(job, now);
            var replayDecisions = composition
                .ReplayFailures.Select(failure =>
                    (
                        Decision: BundleSealFailurePolicy.Decide(
                            failure.Unreadable
                                ? BundleSealStage.ReplayRead
                                : BundleSealStage.ReplayDecode,
                            failure.Exception,
                            retry
                        ),
                        failure.Exception
                    )
                )
                .ToList();
            // A runtime failure on any replay decides the Run; an earlier transient must not mask it.
            var holding = replayDecisions
                .Where(item => item.Decision.Disposition != BundleSealFailureDisposition.Degrade)
                .OrderBy(item => item.Decision.Cause == BundleSealFailureCause.Environment ? 0 : 1)
                .ToList();
            if (holding.Count > 0)
            {
                ApplyJobFailure(runId, holding[0].Decision, holding[0].Exception);
                return false;
            }

            stage = BundleSealStage.PayloadEncode;
            Probe(stage, runId);
            _composer.FitToBudget(composition);
            if (
                BundleSealConvergence.Resolve(
                    jobFacts,
                    secondsUntilInputDeadline,
                    BundleSealInputObservation.ReplayPayload(
                        composition.Payload.Degradation.ReplayOmittedBattleIds.Count
                    )
                ) == BundleSealConvergenceDecision.Wait
            )
                return false;
            foreach (var (decision, exception) in replayDecisions)
                LogFailure(decision, exception, runId);

            composition.Payload.Degradation.ScreenshotOmitted =
                job.ScreenshotRequested && screenshot == null;
            var encodedPayload = RunPayloadV5Codec.Encode(composition.Payload);
            if (
                BundleSealConvergence.Resolve(
                    jobFacts,
                    secondsUntilInputDeadline,
                    BundleSealInputObservation.EncodedPayload(
                        encodedPayload.Length > BundleLimitsV5.MaxRunBytes
                    )
                ) == BundleSealConvergenceDecision.MarkTerminal
            )
            {
                MarkJobTerminal(runId, "minimal_run_payload_too_large", null);
                return false;
            }

            stage = BundleSealStage.Allocation;
            Probe(stage, runId);
            var allocation =
                job.BundleId != null && job.CreatedAtMs.HasValue
                    ? new BundleAllocationRecord(job.BundleId, job.CreatedAtMs.Value)
                    : _queueStore.EnsureAllocation(
                        job.RunId,
                        _ulid.Next(),
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    );

            stage = BundleSealStage.Build;
            Probe(stage, runId);
            var built = BundleV5Codec.Build(
                new BundleBuildInputV5
                {
                    BundleId = allocation.BundleId,
                    CreatedAtMs = allocation.CreatedAtMs,
                    RunId = runId,
                    PlayerAccountId = playerAccountId,
                    Battles = composition.Projections,
                    RunPayload = encodedPayload,
                    Screenshot = screenshot,
                }
            );
            _ = BundleV5Codec.Open(built.Bytes);

            stage = BundleSealStage.Publish;
            Probe(stage, runId);
            // Publication cannot depend on file recovery having run earlier in the pass.
            Directory.CreateDirectory(_outboxRoot);
            var fileName = allocation.BundleId + ".bundle";
            var finalPath = Path.Combine(_outboxRoot, fileName);
            WriteAtomically(finalPath + ".tmp", finalPath, built.Bytes);
            if (
                !_queueStore.PublishOutbox(
                    allocation,
                    new BundleOutboxPublishRecord(
                        allocation.BundleId,
                        job.RunId,
                        fileName,
                        built.Sha256Hex,
                        built.ContentDigest,
                        built.Bytes.Length,
                        screenshot != null
                    ),
                    DateTimeOffset.UtcNow
                )
            )
            {
                if (!_queueStore.ContainsOutbox(allocation.BundleId))
                    File.Delete(finalPath);
                return false;
            }
            BundlePipelineLog.Info(
                new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.seal.succeeded"
                ),
                runId,
                allocation.BundleId
            );
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            RecordJobFailure(runId, job, stage, ex);
            return false;
        }
    }

    private void EnsureSealJobs() => _queueStore.EnsureEligibleJobs(InputConvergenceWindow);

    private void ApplyScreenshotTerminals()
    {
        while (_screenshotTerminals.TryDequeue(out var terminal))
        {
            if (string.IsNullOrWhiteSpace(terminal.RunId))
                continue;
            if (terminal.MetadataPersisted)
                _queueStore.UpdateScreenshotState(terminal.RunId!, BundleScreenshotState.Available);
            else if (terminal.ArtifactStatus == ScreenshotArtifactStatus.Unavailable)
                _queueStore.UpdateScreenshotState(
                    terminal.RunId!,
                    BundleScreenshotState.Unavailable
                );
        }
    }

    private void RecoverFiles()
    {
        Directory.CreateDirectory(_outboxRoot);
        foreach (var temp in Directory.EnumerateFiles(_outboxRoot, "*.bundle.tmp"))
        {
            Maintain(
                BundleSealStage.FileRecovery,
                () =>
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(temp) >= OrphanFileRetention)
                        File.Delete(temp);
                }
            );
        }

        foreach (var file in Directory.EnumerateFiles(_outboxRoot, "*.bundle"))
            RecoverOrphan(file);

        foreach (var row in _queueStore.ListPendingForValidation())
            ValidatePending(row);
    }

    private void RecoverOrphan(string file)
    {
        try
        {
            var bundleId = Path.GetFileNameWithoutExtension(file);
            if (_queueStore.ContainsOutbox(bundleId))
                return;
            Probe(BundleSealStage.OrphanAdoption, bundleId);
            var bytes = File.ReadAllBytes(file);
            var opened = BundleV5Codec.Open(bytes);
            var job = _queueStore.ReadJob(opened.Manifest.Run.RunId);
            if (
                job?.BundleId != opened.Manifest.BundleId
                || job.CreatedAtMs != opened.Manifest.CreatedAtMs
                || !string.Equals(
                    job.PlayerAccountId,
                    opened.Manifest.Run.PlayerAccountId,
                    StringComparison.Ordinal
                )
            )
            {
                DeleteExpiredOrphan(file);
                return;
            }
            _queueStore.PublishOutbox(
                new BundleAllocationRecord(job.BundleId!, job.CreatedAtMs!.Value),
                new BundleOutboxPublishRecord(
                    opened.Manifest.BundleId,
                    opened.Manifest.Run.RunId,
                    Path.GetFileName(file),
                    opened.Sha256Hex,
                    opened.ContentDigest,
                    bytes.Length,
                    opened.Screenshot != null
                ),
                DateTimeOffset.UtcNow
            );
        }
        catch (Exception ex)
        {
            var decision = BundleSealFailurePolicy.Decide(
                BundleSealStage.OrphanAdoption,
                ex,
                default
            );
            LogFailure(decision, ex, null);
            // Only an unreadable Bundle is an orphan; a runtime that cannot open it keeps it.
            if (decision.Disposition == BundleSealFailureDisposition.Degrade)
                Maintain(BundleSealStage.FileRecovery, () => DeleteExpiredOrphan(file));
        }
    }

    private void ValidatePending(BundleOutboxStateRecord row)
    {
        Exception? failure = null;
        try
        {
            Probe(BundleSealStage.PendingValidation, row.RunId);
            var path = Path.Combine(_outboxRoot, row.FileName);
            if (
                File.Exists(path)
                && BundleV5Codec.Open(File.ReadAllBytes(path)).Manifest.BundleId == row.BundleId
            )
                return;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        // A missing or mismatched file is an observed invalid artifact (no exception).
        var decision = BundleSealFailurePolicy.Decide(
            BundleSealStage.PendingValidation,
            failure,
            default
        );
        LogFailure(decision, failure, row.RunId);
        if (decision.Disposition != BundleSealFailureDisposition.Reseal)
            return;
        Maintain(
            BundleSealStage.FileRecovery,
            () =>
                _queueStore.FailOutboxAndScheduleReseal(
                    row.BundleId,
                    row.RunId,
                    decision.Code,
                    DateTimeOffset.UtcNow
                )
        );
    }

    private static void DeleteExpiredOrphan(string path)
    {
        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= OrphanFileRetention)
            File.Delete(path);
    }

    private ScreenshotSource? TryReadScreenshot(string runId)
    {
        var artifact = _screenshotStore.TryGetLatestPrimaryForRun(runId);
        if (artifact == null)
            return null;
        var absolute = Path.GetFullPath(Path.Combine(_screenshotRoot, artifact.ImageRelativePath));
        var root = Path.GetFullPath(_screenshotRoot) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(root, StringComparison.Ordinal) || !File.Exists(absolute))
            return null;
        return new ScreenshotSource(absolute, artifact.CapturedAtUtc.ToUnixTimeMilliseconds());
    }

    private void MarkJobTerminal(string runId, string code, Exception? exception)
    {
        try
        {
            _queueStore.RecordSealFailure(
                runId,
                BundleSealJobState.TerminalFailure,
                code,
                exception?.Message,
                1,
                DateTimeOffset.UtcNow
            );
        }
        catch (Exception ex)
        {
            BundlePipelineLog.Warn(
                new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.reconcile.failed",
                    storm: ["category"]
                ),
                "seal_failure_record_failed",
                ex,
                runId
            );
        }
        BundlePipelineLog.Warn(
            new BppLogEvent(
                BppLogFeatureScope.BundlePipeline,
                "bundle_pipeline.seal.terminal",
                storm: ["category"]
            ),
            code,
            exception,
            runId
        );
    }

    private void RecordJobFailure(
        string runId,
        BundleSealJobRecord? job,
        BundleSealStage stage,
        Exception exception
    )
    {
        try
        {
            job ??= _queueStore.ReadJob(runId);
        }
        catch (Exception readFailure)
        {
            // Without retry facts the original failure is still classified and recorded below.
            BundlePipelineLog.Warn(
                new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.reconcile.failed",
                    storm: ["category"]
                ),
                "seal_job_read_failed",
                readFailure,
                runId
            );
        }
        var decision = BundleSealFailurePolicy.Decide(
            stage,
            exception,
            job == null ? default : RetryFacts(job, DateTimeOffset.UtcNow)
        );
        ApplyJobFailure(runId, decision, exception);
    }

    private void ApplyJobFailure(
        string runId,
        BundleSealFailureDecision decision,
        Exception? exception
    )
    {
        var state = decision.Disposition switch
        {
            BundleSealFailureDisposition.Terminal => BundleSealJobState.TerminalFailure,
            _ => BundleSealJobState.Waiting,
        };
        try
        {
            _queueStore.RecordSealFailure(
                runId,
                state,
                decision.Code,
                decision.Detail,
                decision.Attempts,
                DateTimeOffset.UtcNow
            );
        }
        catch (Exception ex)
        {
            BundlePipelineLog.Warn(
                new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.reconcile.failed",
                    storm: ["category"]
                ),
                "seal_failure_record_failed",
                ex,
                runId
            );
        }
        LogFailure(decision, exception, runId);
    }

    private void Maintain(BundleSealStage stage, Action action)
    {
        try
        {
            Probe(stage, string.Empty);
            action();
        }
        catch (Exception ex)
        {
            LogFailure(BundleSealFailurePolicy.Decide(stage, ex, default), ex, null);
        }
    }

    private void Probe(BundleSealStage stage, string subject) =>
        _faultProbe?.Invoke(stage, subject);

    private static BundleSealRetryFacts RetryFacts(BundleSealJobRecord job, DateTimeOffset now) =>
        new(job.LastErrorCode, job.Attempts, (float)(now - job.InputDeadlineAtUtc).TotalSeconds);

    private static void LogFailure(
        BundleSealFailureDecision decision,
        Exception? exception,
        string? runId
    ) =>
        BundlePipelineLog.Warn(
            decision.Log switch
            {
                BundleSealFailureLog.Deferred => new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.seal.deferred",
                    storm: ["category"]
                ),
                BundleSealFailureLog.EnvironmentBlocked => new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.seal.environment_blocked",
                    storm: ["category"]
                ),
                BundleSealFailureLog.Degraded => new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.seal.degraded",
                    storm: ["category"]
                ),
                BundleSealFailureLog.Terminal => new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.seal.terminal",
                    storm: ["category"]
                ),
                _ => new BppLogEvent(
                    BppLogFeatureScope.BundlePipeline,
                    "bundle_pipeline.reconcile.failed",
                    storm: ["category"]
                ),
            },
            decision.Code,
            exception,
            runId
        );

    private static void WriteAtomically(string tempPath, string finalPath, byte[] bytes)
    {
        using (
            var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)
        )
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, finalPath);
    }

    private sealed record ScreenshotSource(string AbsolutePath, long CapturedAtMs);
}
