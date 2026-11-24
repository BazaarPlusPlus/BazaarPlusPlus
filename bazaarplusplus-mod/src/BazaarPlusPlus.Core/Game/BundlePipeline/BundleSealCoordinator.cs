#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunScreenshot;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.BundlePipeline;

public sealed class BundleSealCoordinator : IBppFeature, IDisposable
{
    private static readonly TimeSpan InputConvergenceWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OrphanFileRetention = TimeSpan.FromHours(24);
    private readonly IBppServices _services;
    private readonly string _databasePath;
    private readonly string _screenshotRoot;
    private readonly string _outboxRoot;
    private readonly BundleQueueStore _queueStore;
    private readonly RunScreenshotSqliteStore _screenshotStore;
    private readonly RunPayloadComposer _composer;
    private readonly IBundleScreenshotEncoder _screenshotEncoder;
    private readonly UlidV6Generator _ulid = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly ConcurrentQueue<ScreenshotCaptureTerminal> _screenshotTerminals = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Action<BundleSealStage, string>? _faultProbe;
    private readonly string? _bundlerVersion;
    private readonly string? _platform;

    private readonly ConcurrentDictionary<string, ReplayWait> _replayWaits = new(
        StringComparer.Ordinal
    );

    private readonly DateTimeOffset _launchedAtUtc;
    private IDisposable? _runInitialized;
    private IDisposable? _runLifecycle;
    private IDisposable? _replayDrained;
    private IDisposable? _screenshotTerminal;
    private IDisposable? _runCompleted;
    private IDisposable? _jobRecreated;
    private Task? _worker;
    private int _wakePending;

    private int _discoveryRequested = 1;
    private readonly Stopwatch _sinceDiscovery = new();
    private volatile bool _started;
    private bool _filesRecovered;
    private volatile bool _paused;

    // Use the Host's database options so every connection opens the database in the same journal mode.
    public BundleSealCoordinator(
        IBppServices services,
        RunPayloadComposer composer,
        IBundleScreenshotEncoder screenshotEncoder,
        Action<BundleSealStage, string>? faultProbe,
        SqliteOpenOptions databaseOptions,
        string? bundlerVersion = null,
        string? platform = null
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _screenshotEncoder =
            screenshotEncoder ?? throw new ArgumentNullException(nameof(screenshotEncoder));
        _faultProbe = faultProbe;
        _bundlerVersion = string.IsNullOrWhiteSpace(bundlerVersion) ? null : bundlerVersion.Trim();
        _platform = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();
        if (databaseOptions == null)
            throw new ArgumentNullException(nameof(databaseOptions));
        _launchedAtUtc = DateTimeOffset.UtcNow;
        var dataRoot = services.Paths.RequireDataRoot();
        _databasePath = PathConstants.RunLogDatabase(dataRoot);
        _screenshotRoot = PathConstants.Screenshots(dataRoot);
        _outboxRoot = PathConstants.BundleOutbox(dataRoot);
        _queueStore = new BundleQueueStore(_databasePath, databaseOptions);
        _screenshotStore = new RunScreenshotSqliteStore(_databasePath, databaseOptions);
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
        _runCompleted = _services.EventBus.Subscribe<RunLogRunCompleted>(_ => RequestDiscovery());
        _jobRecreated = _services.EventBus.Subscribe<BundleSealJobRecreated>(_ =>
            RequestDiscovery()
        );
        _sinceDiscovery.Start();
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
        _runCompleted?.Dispose();
        _jobRecreated?.Dispose();
        _shutdown.Cancel();
        _wake.Release();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(3));
        }
        catch { }
        _shutdown.Dispose();
        _wake.Dispose();
    }

    /// <summary>An in-flight pass finishes when the Host goes into the background.</summary>
    public void Pause()
    {
        if (!_started || _paused)
            return;
        _paused = true;
        Signal();
    }

    public void Resume()
    {
        if (!_started || !_paused)
            return;
        _paused = false;
        RequestDiscovery();
    }

    public void Signal()
    {
        // Run Logging can still complete a Run during shutdown, after this worker is gone.
        if (!_started)
            return;
        try
        {
            if (Interlocked.Exchange(ref _wakePending, 1) == 0)
                _wake.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed between the check and the release; the next launch discovers the Run.
        }
    }

    private void RequestDiscovery()
    {
        Interlocked.Exchange(ref _discoveryRequested, 1);
        Signal();
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken) =>
        await RunPassAsync(discover: true, cancellationToken).ConfigureAwait(false);

    private async Task<PassResult> RunPassAsync(bool discover, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Maintain(BundleSealStage.ScreenshotTerminal, ApplyScreenshotTerminals);
        // Later file corruption is handled by the upload's length and digest checks.
        if (!_filesRecovered)
            Maintain(BundleSealStage.FileRecovery, RecoverFiles);
        var discovered = !discover || Maintain(BundleSealStage.JobDiscovery, EnsureSealJobs);
        var maintenanceFailed = !discovered || !_filesRecovered;
        var published = false;
        try
        {
            var waiting = ListWaitingJobs();
            foreach (var key in _replayWaits.Keys)
            {
                if (!waiting.Any(job => string.Equals(job.RunId, key, StringComparison.Ordinal)))
                    _replayWaits.TryRemove(key, out _);
            }
            HashSet<string>? attempted = null;
            foreach (var job in waiting)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Only this worker writes the retry columns, so the listed row is sufficient for the due check.
                if (!IsDue(job, DateTimeOffset.UtcNow))
                    continue;
                (attempted ??= new(StringComparer.Ordinal)).Add(job.RunId);
                try
                {
                    published |= await TrySealAsync(job.RunId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    RecordJobFailure(job.RunId, null, BundleSealStage.Attempt, ex);
                }
            }
            return new PassResult(
                discovered,
                maintenanceFailed,
                SecondsUntilAttempt(attempted != null ? ListWaitingJobs() : waiting, attempted)
            );
        }
        finally
        {
            if (published)
                _services.EventBus.Publish(new UploadArmRequested());
        }
    }

    private IReadOnlyList<BundleSealJobRecord> ListWaitingJobs()
    {
        var jobs = _queueStore.ListWaitingJobs(out var invalidRows);
        foreach (var failure in invalidRows)
            LogFailure(
                BundleSealFailurePolicy.Decide(BundleSealStage.JobRead, failure.Exception, default),
                failure.Exception,
                failure.RunId
            );
        return jobs;
    }

    private bool IsDue(BundleSealJobRecord job, DateTimeOffset now) =>
        BundleSealFailurePolicy.IsDue(
            job.LastErrorCode,
            job.Attempts,
            job.LastAttemptAtUtc is { } lastAttempt
                ? (float)(now - lastAttempt).TotalSeconds
                : null,
            (float)(now - _launchedAtUtc).TotalSeconds
        );

    private float? SecondsUntilAttempt(
        IReadOnlyList<BundleSealJobRecord> jobs,
        HashSet<string>? attempted
    )
    {
        var now = DateTimeOffset.UtcNow;
        float? soonest = null;
        foreach (var job in jobs)
        {
            var seconds = BundleSealWakeSchedule.SecondsUntilAttempt(
                job.LastErrorCode,
                job.Attempts,
                job.LastAttemptAtUtc is { } lastAttempt
                    ? (float)(now - lastAttempt).TotalSeconds
                    : null,
                (float)(now - _launchedAtUtc).TotalSeconds,
                (float)(job.InputDeadlineAtUtc - now).TotalSeconds,
                attemptedInPass: attempted?.Contains(job.RunId) == true
            );
            if (seconds is { } value && (soonest == null || value < soonest))
                soonest = value;
        }
        return soonest;
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        var parked = false;
        var wait = Timeout.InfiniteTimeSpan;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _wake
                    .WaitAsync(parked ? Timeout.InfiniteTimeSpan : wait, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _wakePending, 0);
                if (_paused != parked)
                {
                    parked = _paused;
                    BppLog.InfoEvent(
                        parked
                            ? new BppLogEvent(
                                BppLogFeatureScope.BundlePipeline,
                                "bundle_pipeline.worker.paused"
                            )
                            : new BppLogEvent(
                                BppLogFeatureScope.BundlePipeline,
                                "bundle_pipeline.worker.resumed"
                            )
                    );
                }
                if (parked)
                    continue;
                var discover = BundleSealWakeSchedule.DiscoveryDue(
                    Interlocked.Exchange(ref _discoveryRequested, 0) == 1,
                    SecondsSinceDiscovery()
                );
                var pass = await RunPassAsync(discover, cancellationToken).ConfigureAwait(false);
                if (discover && pass.Discovered)
                    _sinceDiscovery.Restart();
                else if (discover)
                    Interlocked.Exchange(ref _discoveryRequested, 1);
                wait = TimeSpan.FromSeconds(
                    BundleSealWakeSchedule.WaitSeconds(
                        pass.SecondsUntilAttempt,
                        SecondsSinceDiscovery(),
                        pass.MaintenanceFailed
                    )
                );
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
                Interlocked.Exchange(ref _discoveryRequested, 1);
                wait = TimeSpan.FromSeconds(BundleSealWakeSchedule.InputWaitPollSeconds);
            }
        }
    }

    private float SecondsSinceDiscovery() => (float)_sinceDiscovery.Elapsed.TotalSeconds;

    private async Task<bool> TrySealAsync(string runId, CancellationToken cancellationToken)
    {
        var job = _queueStore.ReadJob(runId);
        if (job == null || job.State == BundleSealJobState.TerminalFailure)
            return false;
        var now = DateTimeOffset.UtcNow;
        if (!IsDue(job, now))
            return false;
        // Read before any replay file: a completion after this read invalidates the wait below.
        var persistenceGeneration = ReplayPersistenceStateTracker.CompletionGeneration;
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
        if (_replayWaits.TryGetValue(runId, out var replayWait))
        {
            if (
                BundleSealConvergence.Resolve(
                    jobFacts,
                    secondsUntilInputDeadline,
                    BundleSealInputObservation.RememberedReplayPayload(
                        replayWait.ReplayOmittedCount,
                        replayWait.PersistenceGeneration != persistenceGeneration
                    )
                ) == BundleSealConvergenceDecision.Wait
            )
                return false;
            _replayWaits.TryRemove(runId, out _);
        }

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
            ScreenshotSource? source = null;
            if (job.ScreenshotRequested && job.ScreenshotState != BundleScreenshotState.Unavailable)
            {
                source = TryReadScreenshot(runId);
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

            // Omissions counted before the size budget: a replay trimmed to fit is not one that
            // has yet to arrive, so it never holds the Run.
            var replayOmitted = composition.Payload.Degradation.ReplayOmittedBattleIds.Count;
            if (
                BundleSealConvergence.Resolve(
                    jobFacts,
                    secondsUntilInputDeadline,
                    BundleSealInputObservation.ReplayPayload(replayOmitted)
                ) == BundleSealConvergenceDecision.Wait
            )
            {
                _replayWaits[runId] = new ReplayWait(replayOmitted, persistenceGeneration);
                return false;
            }
            foreach (var (decision, exception) in replayDecisions)
                LogFailure(decision, exception, runId);

            stage = BundleSealStage.Screenshot;
            Probe(stage, runId);
            BundleScreenshotBuildInputV6? screenshot = null;
            if (source != null)
            {
                screenshot = await _screenshotEncoder
                    .EncodeAsync(source.AbsolutePath, source.CapturedAtMs, cancellationToken)
                    .ConfigureAwait(false);
                _queueStore.UpdateScreenshotState(
                    runId,
                    screenshot == null
                        ? BundleScreenshotState.Unavailable
                        : BundleScreenshotState.Available
                );
            }

            stage = BundleSealStage.PayloadEncode;
            Probe(stage, runId);
            var encodedPayload = _composer.EncodeFinal(
                composition,
                screenshotOmitted: job.ScreenshotRequested && screenshot == null
            );
            if (
                BundleSealConvergence.Resolve(
                    jobFacts,
                    secondsUntilInputDeadline,
                    BundleSealInputObservation.EncodedPayload(
                        encodedPayload.Length > BundleLimitsV6.MaxRunBytes
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
            var built = BundleV6Codec.Build(
                new BundleBuildInputV6
                {
                    BundleId = allocation.BundleId,
                    CreatedAtMs = allocation.CreatedAtMs,
                    BundlerVersion = _bundlerVersion,
                    Platform = _platform,
                    RunId = runId,
                    PlayerAccountId = playerAccountId,
                    Battles = composition.Projections,
                    RunPayload = encodedPayload,
                    Screenshot = screenshot,
                }
            );
            _ = BundleV6Codec.Open(built.Bytes);

            stage = BundleSealStage.Publish;
            Probe(stage, runId);
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

    // Run Message Stream rows are only useful while the Run can still upload them (ADR-0028).
    private void EnsureSealJobs()
    {
        _queueStore.EnsureEligibleJobs(InputConvergenceWindow);
        _queueStore.DeleteSpentMessageStreams();
    }

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
        _filesRecovered = true;
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
            var opened = BundleV6Codec.Open(bytes);
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
                && BundleV6Codec.Open(File.ReadAllBytes(path)).Manifest.BundleId == row.BundleId
            )
                return;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
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

    private bool Maintain(BundleSealStage stage, Action action)
    {
        try
        {
            Probe(stage, string.Empty);
            action();
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(BundleSealFailurePolicy.Decide(stage, ex, default), ex, null);
            return false;
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

    // Discovered also covers passes where discovery was not requested.
    private readonly record struct PassResult(
        bool Discovered,
        bool MaintenanceFailed,
        float? SecondsUntilAttempt
    );

    private readonly record struct ReplayWait(int ReplayOmittedCount, long PersistenceGeneration);
}
