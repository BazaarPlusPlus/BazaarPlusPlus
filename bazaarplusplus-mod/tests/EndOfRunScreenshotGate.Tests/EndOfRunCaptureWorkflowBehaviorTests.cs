#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Infrastructure;
using BepInEx.Logging;

internal static class EndOfRunCaptureWorkflowBehaviorTests
{
    internal static void Run()
    {
        Missing_surface_is_fail_open();
        Reveal_and_success_flow();
        Clean_frame_fallback_persists_artifact_and_reports_degraded();
        Capture_timeout_starts_when_capture_is_invoked();
        Retryable_failures_get_one_retry_only();
        Adapter_non_retryable_failure_fails_open();
        Reveal_and_capture_timeouts_fail_open_without_retry();
        Metadata_failure_and_timeout_keep_the_verified_artifact();
        Unexpected_failure_after_capture_preserves_the_verified_artifact();
        Unexpected_failure_before_capture_releases_the_active_attempt();
        Failed_unblock_is_retried_without_blocking_continue();
        Navigation_after_frame_acquisition_keeps_background_capture();
        Context_expiry_cancels_and_cleans_up_a_late_artifact();
        Driver_detach_releases_the_active_attempt();
        Reset_cancels_the_old_generation_and_allows_a_new_run();
        Terminal_outcome_is_emitted_once();
    }

    private static void Capture_timeout_starts_when_capture_is_invoked()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Surface.NextAttemptHasStarted = false;
        fixture.Core.OnFrame(fixture.Context);
        var attempt = fixture.Surface.LastAttempt!;

        fixture.Clock.Advance(20f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(
            attempt.CancelCount == 0 && fixture.Surface.IsBlocked,
            "Next-loop and end-of-frame scheduling must not consume the capture timeout."
        );

        attempt.HasCaptureStarted = true;
        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(14.99f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.CancelCount == 0, "Capture should retain the full 15-second timeout.");
        fixture.Clock.Advance(0.02f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.CancelCount == 1, "Capture should time out after its own deadline.");
    }

    private static void Clean_frame_fallback_persists_artifact_and_reports_degraded()
    {
        using var log = new TestLogCapture();
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;
        AcquireFrame(fixture);
        fixture.Files.MarkUsable("clean-frame-fallback.png");
        attempt.Complete(
            Success("clean-frame-fallback.png", ScreenshotCaptureReasonCode.CleanFrameDeadline)
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(
            fixture.Persistence.CallCount == 1,
            "A clean-frame fallback artifact must persist metadata."
        );

        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);

        var terminals = log
            .Events.Where(entry =>
                entry.Contains("event=screenshots.capture.degraded", StringComparison.Ordinal)
            )
            .ToArray();
        Assert(terminals.Length == 1, "A clean-frame fallback must emit one degraded outcome.");
        Assert(
            terminals[0].Contains("reason_code=clean_frame_deadline", StringComparison.Ordinal)
                && terminals[0].Contains("artifact_status=complete", StringComparison.Ordinal)
                && !fixture.Files.Deleted.Contains("clean-frame-fallback.png"),
            "A clean-frame fallback must retain its complete artifact and degradation reason."
        );
    }

    private static void Missing_surface_is_fail_open()
    {
        var core = new EndOfRunCaptureWorkflowCore<FakeScreen>(
            new FakePersistence(),
            new FakeClock(),
            new FakeFileSystem(),
            EndOfRunCapturePolicy.Default
        );
        var screen = new FakeScreen { Id = 1 };

        Assert(
            !core.ShouldBlockContinue(screen, new EndOfRunCaptureContext(true, "run", "hero")),
            "Patches must fail open before the runtime surface is mounted."
        );
    }

    private static void Reveal_and_success_flow()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.RevealNotStarted);

        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 0, "Capture must wait for native reveal start.");
        Assert(
            fixture.Core.ShouldBlockContinue(fixture.Screen, fixture.Context),
            "Continue should remain blocked while reveal has not started."
        );

        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Ready reveal should begin exactly one attempt.");
        Assert(fixture.Surface.IsBlocked, "Capture attempt should block Continue.");

        var attempt = fixture.Surface.LastAttempt!;
        Assert(attempt.LeaseActive, "Suppression must be active before frame acquisition.");
        attempt.AcquireFrame();
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.RestoreCount == 1, "Frame acquisition must restore UI immediately.");
        Assert(
            !attempt.LeaseActive,
            "Frame acquisition must release suppression without waiting for PNG encoding."
        );
        Assert(
            !fixture.Surface.IsBlocked,
            "Continue should release as soon as settled pixels are detached from Unity."
        );
        Assert(
            !fixture.Core.ShouldBlockContinue(fixture.Screen, fixture.Context),
            "PNG encoding must not keep the native Continue action blocked."
        );
        Assert(
            fixture.Persistence.CallCount == 0,
            "Metadata persistence must wait for the encoded PNG artifact."
        );

        fixture.Files.MarkUsable("success.png");
        attempt.Complete(Success("success.png"));
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.RestoreCount == 1, "UI must not be restored twice after PNG encoding.");
        Assert(fixture.Persistence.CallCount == 1, "Verified artifact should persist metadata.");
        Assert(!fixture.Surface.IsBlocked, "Metadata persistence must remain in the background.");

        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);
        Assert(!fixture.Surface.IsBlocked, "Successful persistence should release Continue.");
        Assert(
            !fixture.Core.ShouldBlockContinue(fixture.Screen, fixture.Context),
            "Terminal success must remain fail-open."
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Repeated frames must not duplicate capture.");
    }

    private static void Adapter_non_retryable_failure_fails_open()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;
        attempt.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.ContextExpired)
        );

        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(10f);
        fixture.Core.OnFrame(fixture.Context);

        Assert(!fixture.Surface.IsBlocked, "Context expiry from the adapter must fail open.");
        Assert(fixture.Surface.BeginCount == 1, "Context expiry from the adapter must not retry.");
        Assert(attempt.RestoreCount == 1, "Failure must restore UI once.");
    }

    private static void Retryable_failures_get_one_retry_only()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Surface.ThrowOnNextBegin = new InvalidOperationException("sync capture failure");
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Synchronous failure should count as attempt one.");
        Assert(fixture.Surface.IsBlocked, "A scheduled retry should keep Continue blocked.");

        fixture.Clock.Advance(0.99f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 1, "Retry should respect the cooldown.");
        fixture.Clock.Advance(0.02f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 2, "First failure should retry exactly once.");

        var secondAttempt = fixture.Surface.LastAttempt!;
        secondAttempt.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(
                ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                new InvalidOperationException("task fault")
            )
        );
        fixture.Core.OnFrame(fixture.Context);
        Assert(!secondAttempt.LeaseActive, "Final attempt failure must release suppression.");
        Assert(!fixture.Surface.IsBlocked, "Second retryable failure must fail open.");
        fixture.Clock.Advance(10f);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 2, "Retry budget must be capped at two attempts.");

        var nullFixture = ReadyFixture();
        var firstNullAttempt = nullFixture.Surface.LastAttempt!;
        firstNullAttempt.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.CaptureReturnedNull)
        );
        nullFixture.Core.OnFrame(nullFixture.Context);
        Assert(
            !firstNullAttempt.LeaseActive,
            "A failed attempt must release its independent preparation lease."
        );
        nullFixture.Clock.Advance(1f);
        nullFixture.Core.OnFrame(nullFixture.Context);
        Assert(nullFixture.Surface.BeginCount == 2, "A pre-frame null capture should retry once.");
        Assert(
            nullFixture.Surface.LastAttempt!.LeaseActive,
            "A retry must acquire a fresh preparation lease before it can capture."
        );

        var unusableFixture = ReadyFixture();
        unusableFixture.Surface.LastAttempt!.AcquireFrame();
        unusableFixture.Core.OnFrame(unusableFixture.Context);
        unusableFixture.Surface.LastAttempt.Complete(Success("empty.png"));
        unusableFixture.Core.OnFrame(unusableFixture.Context);
        unusableFixture.Clock.Advance(10f);
        unusableFixture.Core.OnFrame(unusableFixture.Context);
        Assert(
            unusableFixture.Surface.BeginCount == 1 && !unusableFixture.Surface.IsBlocked,
            "A post-release artifact failure must fail open without recapturing a later page."
        );
    }

    private static void Reveal_and_capture_timeouts_fail_open_without_retry()
    {
        var revealFixture = new Fixture();
        revealFixture.Core.OnEndOfRunInitializing();
        revealFixture.Core.ObserveRevealStarted(revealFixture.Screen);
        revealFixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.RevealInProgress);
        revealFixture.Core.OnFrame(revealFixture.Context);
        revealFixture.Clock.Advance(20f);
        revealFixture.Core.OnFrame(revealFixture.Context);
        Assert(
            revealFixture.Surface.BeginCount == 0 && !revealFixture.Surface.IsBlocked,
            "Known in-progress reveal should fail open at its deadline without capture."
        );

        var captureFixture = ReadyFixture();
        var attempt = captureFixture.Surface.LastAttempt!;
        captureFixture.Clock.Advance(15f);
        captureFixture.Core.OnFrame(captureFixture.Context);
        Assert(
            attempt.CancelCount == 1,
            "Capture timeout should cancel the active adapter attempt."
        );
        Assert(!captureFixture.Surface.IsBlocked, "Capture timeout should immediately fail open.");
        Assert(!attempt.LeaseActive, "Capture timeout must release suppression.");
        captureFixture.Clock.Advance(100f);
        captureFixture.Core.OnFrame(captureFixture.Context);
        Assert(captureFixture.Surface.BeginCount == 1, "Capture timeout must not retry.");

        captureFixture.Files.MarkUsable("late-timeout.png");
        attempt.Complete(Success("late-timeout.png"));
        WaitUntil(
            () => captureFixture.Files.Deleted.Contains("late-timeout.png"),
            "Timed-out late artifact should be deleted."
        );
    }

    private static void Metadata_failure_and_timeout_keep_the_verified_artifact()
    {
        var failed = ReadyFixture();
        AcquireFrame(failed);
        failed.Files.MarkUsable("metadata-failed.png");
        failed.Surface.LastAttempt!.Complete(Success("metadata-failed.png"));
        failed.Core.OnFrame(failed.Context);
        failed.Persistence.CompleteNext(
            ScreenshotMetadataPersistenceOutcome.Failed(
                new InvalidOperationException("metadata failed")
            )
        );
        failed.Core.OnFrame(failed.Context);
        Assert(!failed.Surface.IsBlocked, "Metadata failure should terminate degraded.");
        Assert(failed.Surface.BeginCount == 1, "Metadata failure must not recapture.");
        Assert(
            !failed.Files.Deleted.Contains("metadata-failed.png"),
            "Metadata failure must preserve the verified file."
        );

        var timedOut = ReadyFixture();
        AcquireFrame(timedOut);
        timedOut.Files.MarkUsable("metadata-timeout.png");
        timedOut.Surface.LastAttempt!.Complete(Success("metadata-timeout.png"));
        timedOut.Core.OnFrame(timedOut.Context);
        timedOut.Clock.Advance(5f);
        timedOut.Core.OnFrame(timedOut.Context);
        Assert(!timedOut.Surface.IsBlocked, "Metadata timeout should terminate degraded.");
        Assert(timedOut.Surface.BeginCount == 1, "Metadata timeout must not recapture.");
        Assert(
            !timedOut.Files.Deleted.Contains("metadata-timeout.png"),
            "Metadata timeout must preserve the verified file."
        );
    }

    private static void Unexpected_failure_after_capture_preserves_the_verified_artifact()
    {
        var fixture = ReadyFixture();
        AcquireFrame(fixture);
        fixture.Files.MarkUsable("unexpected-metadata.png");
        fixture.Surface.LastAttempt!.Complete(Success("unexpected-metadata.png"));
        fixture.Core.OnFrame(fixture.Context);

        fixture.Core.FailOpenUnexpected(new InvalidOperationException("unexpected"));

        Assert(!fixture.Surface.IsBlocked, "Unexpected failures must release Continue.");
        Assert(fixture.Surface.BeginCount == 1, "Unexpected failures must not recapture.");
        Assert(
            !fixture.Files.Deleted.Contains("unexpected-metadata.png"),
            "A verified artifact must survive fail-open during metadata persistence."
        );
    }

    private static void Unexpected_failure_before_capture_releases_the_active_attempt()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;

        fixture.Core.FailOpenUnexpected(new InvalidOperationException("unexpected pre-frame"));

        Assert(attempt.CancelCount == 1, "Unexpected failure should cancel the active attempt.");
        Assert(!attempt.LeaseActive, "Unexpected failure must release suppression.");
        Assert(!fixture.Surface.IsBlocked, "Unexpected failure must fail open Continue.");
    }

    private static void Failed_unblock_is_retried_without_blocking_continue()
    {
        var fixture = ReadyFixture();
        fixture.Surface.UnblockFailuresRemaining = 1;
        fixture.Surface.LastAttempt!.AcquireFrame();
        fixture.Core.OnFrame(fixture.Context);

        Assert(
            !fixture.Core.ShouldBlockContinue(fixture.Screen, fixture.Context),
            "A failed physical detach must still fail open logically."
        );
        Assert(!fixture.Surface.IsBlocked, "A later frame should retry physical blocker detach.");
        Assert(fixture.Surface.UnblockCount >= 2, "Failed unblock should be retried.");
    }

    private static void Navigation_after_frame_acquisition_keeps_background_capture()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;
        AcquireFrame(fixture);
        fixture.Screen.IsActive = false;
        fixture.Surface.ActiveScreen = null;

        fixture.Files.MarkUsable("background.png");
        attempt.Complete(Success("background.png"));
        fixture.Core.OnFrame(fixture.Context);
        Assert(
            fixture.Persistence.CallCount == 1,
            "Leaving the summary after frame acquisition must not cancel PNG completion."
        );
        Assert(attempt.CancelCount == 0, "Detached background capture must not be canceled.");

        fixture.Persistence.CompleteNext(ScreenshotMetadataPersistenceOutcome.Saved());
        fixture.Core.OnFrame(fixture.Context);
        Assert(
            !fixture.Files.Deleted.Contains("background.png"),
            "A captured final frame must survive navigation and metadata completion."
        );
    }

    private static void Context_expiry_cancels_and_cleans_up_a_late_artifact()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;
        fixture.Screen.IsActive = false;
        fixture.Surface.ActiveScreen = null;
        fixture.Core.OnFrame(fixture.Context);
        Assert(attempt.CancelCount == 1, "Context expiry should cancel the active attempt.");
        Assert(!attempt.LeaseActive, "Context expiry must release suppression.");
        Assert(!fixture.Surface.IsBlocked, "Context expiry should release stale blocker.");
        Assert(fixture.Surface.BeginCount == 1, "Context expiry must not retry.");

        fixture.Files.MarkUsable("late-context.png");
        attempt.Complete(Success("late-context.png"));
        WaitUntil(
            () => fixture.Files.Deleted.Contains("late-context.png"),
            "Context-expired late artifact should be deleted."
        );
    }

    private static void Driver_detach_releases_the_active_attempt()
    {
        var fixture = ReadyFixture();
        var attempt = fixture.Surface.LastAttempt!;

        fixture.Core.DetachSurface(fixture.Surface);

        Assert(attempt.CancelCount == 1, "Driver detach should cancel the active attempt.");
        Assert(!attempt.LeaseActive, "Driver detach must release suppression.");
        Assert(!fixture.Surface.IsBlocked, "Driver detach must fail open Continue.");
    }

    private static void Reset_cancels_the_old_generation_and_allows_a_new_run()
    {
        var fixture = ReadyFixture();
        var oldAttempt = fixture.Surface.LastAttempt!;
        fixture.Core.OnRunStarted();
        Assert(oldAttempt.CancelCount == 1, "Run reset should cancel the old generation.");
        Assert(!oldAttempt.LeaseActive, "Run reset must release old-generation suppression.");
        Assert(!fixture.Surface.IsBlocked, "Run reset should release Continue.");

        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        Assert(fixture.Surface.BeginCount == 2, "New run should get a fresh capture generation.");
    }

    private static void Terminal_outcome_is_emitted_once()
    {
        using var log = new TestLogCapture();
        var fixture = ReadyFixture();
        fixture.Surface.LastAttempt!.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(
                ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                new InvalidOperationException("first")
            )
        );
        fixture.Core.OnFrame(fixture.Context);
        fixture.Clock.Advance(1f);
        fixture.Core.OnFrame(fixture.Context);
        fixture.Surface.LastAttempt!.Complete(
            EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.CaptureReturnedNull)
        );
        fixture.Core.OnFrame(fixture.Context);
        fixture.Core.OnFrame(fixture.Context);

        var terminals = log
            .Events.Where(entry =>
                entry.Contains("event=screenshots.capture.failed", StringComparison.Ordinal)
            )
            .ToArray();
        Assert(terminals.Length == 1, "One run must emit exactly one terminal outcome.");
        Assert(
            terminals[0].Contains("attempt_count=2", StringComparison.Ordinal),
            "Terminal outcome should report the bounded attempt count."
        );
    }

    private static Fixture ReadyFixture()
    {
        var fixture = new Fixture();
        fixture.Core.OnEndOfRunInitializing();
        fixture.Core.ObserveRevealStarted(fixture.Screen);
        fixture.Screen.Readiness = Readiness(EndOfRunCaptureReadinessState.Ready);
        fixture.Core.OnFrame(fixture.Context);
        return fixture;
    }

    private static void AcquireFrame(Fixture fixture)
    {
        fixture.Surface.LastAttempt!.AcquireFrame();
        fixture.Core.OnFrame(fixture.Context);
        Assert(!fixture.Surface.IsBlocked, "Frame acquisition should release Continue.");
    }

    private static EndOfRunCaptureReadinessOutcome Readiness(EndOfRunCaptureReadinessState state) =>
        new(state, null, null);

    private static EndOfRunCaptureAttemptOutcome Success(
        string path,
        ScreenshotCaptureReasonCode? degradationReason = null
    ) =>
        EndOfRunCaptureAttemptOutcome.Succeeded(
            new ScreenshotCaptureResult { ScreenshotId = "shot", FilePath = path },
            degradationReason
        );

    private static void WaitUntil(Func<bool> predicate, string message)
    {
        if (SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(2)))
            return;
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            Surface.ActiveScreen = Screen;
            Core = new EndOfRunCaptureWorkflowCore<FakeScreen>(
                Persistence,
                Clock,
                Files,
                EndOfRunCapturePolicy.Default
            );
            Core.AttachSurface(Surface);
        }

        internal FakeClock Clock { get; } = new();
        internal FakeFileSystem Files { get; } = new();
        internal FakePersistence Persistence { get; } = new();
        internal FakeSurface Surface { get; } = new();
        internal FakeScreen Screen { get; } = new() { Id = 7 };
        internal EndOfRunCaptureWorkflowCore<FakeScreen> Core { get; }
        internal EndOfRunCaptureContext Context { get; } = new(true, "run-7", "Vanessa");
    }

    private sealed class FakeScreen
    {
        internal int Id { get; init; }
        internal bool IsActive { get; set; } = true;
        internal EndOfRunCaptureReadinessOutcome Readiness { get; set; } =
            EndOfRunCaptureWorkflowBehaviorTests.Readiness(
                EndOfRunCaptureReadinessState.RevealNotStarted
            );
    }

    private sealed class FakeSurface : IEndOfRunCaptureSurface<FakeScreen>
    {
        internal FakeScreen? ActiveScreen { get; set; }
        internal int BeginCount { get; private set; }
        internal bool IsBlocked { get; private set; }
        internal ManualAttempt? LastAttempt { get; private set; }
        internal Exception? ThrowOnNextBegin { get; set; }
        internal bool NextAttemptHasStarted { get; set; } = true;
        internal int UnblockFailuresRemaining { get; set; }
        internal int UnblockCount { get; private set; }

        public bool IsAvailable => true;

        public FakeScreen? FindActiveScreen() => ActiveScreen;

        public bool IsScreenActive(FakeScreen screen) => screen.IsActive;

        public int GetScreenId(FakeScreen screen) => screen.Id;

        public EndOfRunCaptureReadinessOutcome GetReadiness(
            FakeScreen screen,
            bool hasRevealStarted
        ) => screen.Readiness;

        public IEndOfRunCaptureAttempt BeginCapture(
            FakeScreen screen,
            ScreenshotCaptureRequest request
        )
        {
            BeginCount++;
            if (ThrowOnNextBegin != null)
            {
                var failure = ThrowOnNextBegin;
                ThrowOnNextBegin = null;
                throw failure;
            }

            return LastAttempt = new ManualAttempt { HasCaptureStarted = NextAttemptHasStarted };
        }

        public void SetContinueBlocked(FakeScreen? screen, bool blocked)
        {
            if (!blocked)
            {
                UnblockCount++;
                if (UnblockFailuresRemaining > 0)
                {
                    UnblockFailuresRemaining--;
                    throw new InvalidOperationException("transient unblock failure");
                }
            }
            IsBlocked = blocked;
        }
    }

    private sealed class ManualAttempt : IEndOfRunCaptureAttempt
    {
        private readonly TaskCompletionSource<bool> _frameAcquired = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<EndOfRunCaptureAttemptOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task FrameAcquired => _frameAcquired.Task;
        public Task<EndOfRunCaptureAttemptOutcome> Completion => _completion.Task;
        public bool HasCaptureStarted { get; set; } = true;
        internal int CancelCount { get; private set; }
        internal int RestoreCount { get; private set; }
        internal bool LeaseActive { get; private set; } = true;

        public void Cancel()
        {
            CancelCount++;
            LeaseActive = false;
        }

        public void RestoreUi()
        {
            RestoreCount++;
            LeaseActive = false;
        }

        internal void AcquireFrame() => _frameAcquired.TrySetResult(true);

        internal void Complete(EndOfRunCaptureAttemptOutcome outcome) =>
            _completion.TrySetResult(outcome);
    }

    private sealed class FakePersistence : IEndOfRunArtifactPersistence
    {
        private readonly Queue<
            TaskCompletionSource<ScreenshotMetadataPersistenceOutcome>
        > _pending = new();

        internal int CallCount { get; private set; }

        public Task<ScreenshotMetadataPersistenceOutcome> PersistAsync(
            ScreenshotCaptureResult capture,
            bool isPrimary
        )
        {
            CallCount++;
            var completion = new TaskCompletionSource<ScreenshotMetadataPersistenceOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _pending.Enqueue(completion);
            return completion.Task;
        }

        internal void CompleteNext(ScreenshotMetadataPersistenceOutcome outcome) =>
            _pending.Dequeue().TrySetResult(outcome);
    }

    private sealed class FakeClock : IEndOfRunCaptureClock
    {
        public float UnscaledSeconds { get; private set; }
        public float RealtimeSeconds { get; private set; }
        public long Milliseconds => (long)Math.Round(RealtimeSeconds * 1000d);

        internal void Advance(float seconds)
        {
            UnscaledSeconds += seconds;
            RealtimeSeconds += seconds;
        }
    }

    private sealed class FakeFileSystem : IEndOfRunCaptureFileSystem
    {
        private readonly HashSet<string> _usable = new(StringComparer.Ordinal);
        internal HashSet<string> Deleted { get; } = new(StringComparer.Ordinal);

        public bool IsUsablePng(string? filePath) => filePath != null && _usable.Contains(filePath);

        public void DeleteIfExists(string? filePath)
        {
            if (filePath != null)
                Deleted.Add(filePath);
        }

        internal void MarkUsable(string path) => _usable.Add(path);
    }

    private sealed class TestLogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("EndOfRunCaptureWorkflow.Tests");
        private readonly List<string> _events = new();

        internal TestLogCapture()
        {
            _source.LogEvent += OnLogEvent;
            BppLog.Install(_source);
        }

        internal IReadOnlyList<string> Events => _events;

        public void Dispose()
        {
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args) =>
            _events.Add(args.Data?.ToString() ?? string.Empty);
    }
}
