#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Upload;
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
    private IDisposable? _runInitialized;
    private IDisposable? _runLifecycle;
    private IDisposable? _replayDrained;
    private IDisposable? _screenshotTerminal;
    private Task? _worker;
    private int _wakePending;
    private bool _started;

    internal BundleSealCoordinator(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
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
        ApplyScreenshotTerminals();
        RecoverFiles();
        EnsureSealJobs();
        foreach (var runId in _queueStore.ListWaitingRunIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TrySealAsync(runId, cancellationToken).ConfigureAwait(false);
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
                    BundlePipelineLogEvents.ReconcileFailed,
                    "reconcile_exception",
                    ex
                );
            }
        }
    }

    private async Task TrySealAsync(string runId, CancellationToken cancellationToken)
    {
        var job = _queueStore.ReadJob(runId);
        if (job == null || job.State == BundleSealJobState.TerminalFailure)
            return;
        var now = DateTimeOffset.UtcNow;
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
            return;

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
                    return;
            }
            MarkJobTerminal(runId, ex.Code, null);
            return;
        }

        BundleScreenshotBuildInputV5? screenshot = null;
        if (job.ScreenshotRequested && job.ScreenshotState != BundleScreenshotState.Unavailable)
        {
            var source = TryReadScreenshot(runId);
            if (source == null)
            {
                var screenshotDecision = BundleSealConvergence.Resolve(
                    jobFacts,
                    secondsUntilInputDeadline,
                    BundleSealInputObservation.Availability(BundleSealInputGate.Screenshot, false)
                );
                if (screenshotDecision == BundleSealConvergenceDecision.Wait)
                    return;
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

        RunPayloadComposition composition;
        try
        {
            composition = _composer.Compose(runId, playerAccountId);
        }
        catch (BundleCompositionException ex)
        {
            MarkJobTerminal(runId, ex.Code, null);
            return;
        }
        catch (Exception ex)
        {
            MarkJobWaiting(runId, "payload_compose_failed", ex.Message);
            return;
        }

        if (
            BundleSealConvergence.Resolve(
                jobFacts,
                secondsUntilInputDeadline,
                BundleSealInputObservation.ReplayPayload(
                    composition.Payload.Degradation.ReplayOmittedBattleIds.Count
                )
            ) == BundleSealConvergenceDecision.Wait
        )
            return;
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
            return;
        }

        var allocation =
            job.BundleId != null && job.CreatedAtMs.HasValue
                ? new BundleAllocationRecord(job.BundleId, job.CreatedAtMs.Value)
                : _queueStore.EnsureAllocation(
                    job.RunId,
                    _ulid.Next(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    DateTimeOffset.UtcNow
                );
        BundleBuildResultV5 built;
        try
        {
            built = BundleV5Codec.Build(
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
        }
        catch (Exception ex)
        {
            MarkJobTerminal(runId, "bundle_build_failed", ex.Message);
            return;
        }

        var fileName = allocation.BundleId + ".bundle";
        var finalPath = Path.Combine(_outboxRoot, fileName);
        var tempPath = finalPath + ".tmp";
        try
        {
            WriteAtomically(tempPath, finalPath, built.Bytes);
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
                return;
            }
            _services.EventBus.Publish(new UploadArmRequested());
            BundlePipelineLog.Info(
                BundlePipelineLogEvents.SealSucceeded,
                runId,
                allocation.BundleId
            );
        }
        catch (Exception ex)
        {
            MarkJobWaiting(runId, "seal_publish_failed", ex.Message);
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
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(temp) >= OrphanFileRetention)
                File.Delete(temp);
        }

        foreach (var file in Directory.EnumerateFiles(_outboxRoot, "*.bundle"))
        {
            var bundleId = Path.GetFileNameWithoutExtension(file);
            if (_queueStore.ContainsOutbox(bundleId))
                continue;
            try
            {
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
                    continue;
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
            catch
            {
                DeleteExpiredOrphan(file);
            }
        }

        var invalid = new List<(string BundleId, string RunId, string Reason)>();
        foreach (var row in _queueStore.ListPendingForValidation())
        {
            var path = Path.Combine(_outboxRoot, row.FileName);
            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException();
                var opened = BundleV5Codec.Open(File.ReadAllBytes(path));
                if (opened.Manifest.BundleId != row.BundleId)
                    throw new InvalidDataException();
            }
            catch
            {
                invalid.Add((row.BundleId, row.RunId, "pending_file_invalid"));
            }
        }
        foreach (var row in invalid)
            _queueStore.FailOutboxAndScheduleReseal(
                row.BundleId,
                row.RunId,
                row.Reason,
                DateTimeOffset.UtcNow
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

    private void MarkJobWaiting(string runId, string code, string? detail) =>
        _queueStore.MarkJobWaiting(runId, code, detail);

    private void MarkJobTerminal(string runId, string code, string? detail)
    {
        _queueStore.MarkJobTerminal(runId, code, detail);
        BundlePipelineLog.Warn(BundlePipelineLogEvents.SealTerminal, code, null, runId);
    }

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
