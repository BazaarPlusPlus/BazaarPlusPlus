using System.Collections;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.Sqlite;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;
using Il2CppTheBazaar.UI.EndOfRun;
using Il2CppTheBazaar.UI.Tooltips;
using MelonLoader;
using UnityEngine;

namespace BazaarPlusPlus.Android.Game.Screenshots;

/// <summary>
/// The Android End-of-Run Capture Workflow: Core's <see cref="EndOfRunCaptureWorkflowCore{TScreen}"/>
/// over the game's end-of-run screen, the desktop's state machine with this host's surface. It
/// captures only while BazaarDB upload is on, since the screenshot exists for the Bundle; the
/// PNG lands under the Data Root's <c>Screenshots/</c> with its <c>run_screenshots</c> row, where
/// Core's seal coordinator reads it. Readiness follows the desktop rule through typed interop
/// reads: reveal started, no screen transition, skills settled, and the cards settled. The game's
/// raw reveal never sets FaceUp here (the desktop's completion patch is not ported), so the cards
/// settle by their animator subtrees' poses, as on the desktop, or by a pose that held from the
/// first sample. When neither holds by the reveal deadline the capture still takes the frame,
/// degraded. The two patches reach it through <c>AndroidPatchHost.Features</c>; the melon ticks
/// it on the main thread.
/// </summary>
internal sealed class AndroidEndOfRunCapture
    : IEndOfRunCaptureSurface<EndOfRunScreenController>,
        IDisposable
{
    private const float ScreenScanIntervalSeconds = 0.5f;
    private const string FaceUpParameter = "FaceUp";
    private static readonly int FaceUpHash = Animator.StringToHash(FaceUpParameter);
    private const ulong HashOffset = 14695981039346656037UL;

    // A pose unchanged for this long since the first sample counts as settled, motion or not.
    private const float StillSeconds = 1.5f;

    // The desktop waits up to 20 s for a reveal; here the deadline captures what is shown, so
    // it waits less before holding Continue no longer.
    private static readonly EndOfRunCapturePolicy Policy = EndOfRunCapturePolicy.Default with
    {
        RevealDeadlineSeconds = 8f,
    };

    private readonly IBppServices _services;
    private readonly Func<bool> _enabled;
    private readonly string _screenshotDirectory;
    private readonly EndOfRunCaptureWorkflowCore<EndOfRunScreenController> _core;
    private readonly EndOfRunVisualStabilityTracker _stability = new();
    private readonly EndOfRunHeavySampleCadence _cadence = new();
    private readonly IDisposable _runInitialized;
    private readonly Il2CppSystem.Action _runStarted;
    private readonly Il2CppSystem.Action _endOfRunInitializing;
    private EndOfRunScreenController? _cachedScreen;
    private float _nextScanAt;
    private int _stabilitySummaryId;
    private readonly List<Transform> _poseNodes = new();
    private int _poseCardCount = -1;
    private ulong _poseSet = HashOffset;
    private bool _hasPose;
    private ulong _lastPose;
    private float _lastPoseChangeAt;
    private string? _bufferedRunId;
    private bool _continueBlocked;
    private bool _disposed;

    public AndroidEndOfRunCapture(
        IBppServices services,
        Func<bool> enabled,
        SqliteOpenOptions databaseOptions
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _screenshotDirectory = PathConstants.Screenshots(services.Paths.RequireDataRoot());
        _core = new EndOfRunCaptureWorkflowCore<EndOfRunScreenController>(
            new EndOfRunArtifactPersistence(services, databaseOptions),
            new UnityClock(),
            new SystemEndOfRunCaptureFileSystem(),
            Policy,
            terminal => services.EventBus.Publish(terminal)
        );
        _runInitialized = services.EventBus.Subscribe<RunInitializedObserved>(observed =>
        {
            if (!string.IsNullOrWhiteSpace(observed.RunId))
                _bufferedRunId = observed.RunId;
        });
        _runStarted = Convert(OnRunStarted);
        _endOfRunInitializing = Convert(OnEndOfRunInitializing);
        Events.RunStarted.AddListener(_runStarted, null);
        Events.EndOfRunScreenInitializing.AddListener(_endOfRunInitializing, null);
        _core.AttachSurface(this);
        RefreshRunId();
    }

    /// <summary>Attempts started, frames read and PNGs written, for the hooks summary.</summary>
    public long Attempts { get; private set; }

    public long FramesRead { get; private set; }

    public long ContinueBlocks { get; private set; }

    public bool IsAvailable => !_disposed;

    /// <summary>Main thread, every frame.</summary>
    public void Tick()
    {
        if (_disposed)
            return;
        try
        {
            _core.OnFrame(ReadContext());
        }
        catch (Exception exception)
        {
            _core.FailOpenUnexpected(exception);
        }
    }

    /// <summary>The Continue patch: true holds the click while the capture is pending.</summary>
    public bool ShouldBlockContinue(EndOfRunScreenController screen)
    {
        if (_disposed || screen == null)
            return false;
        try
        {
            var blocked = _core.ShouldBlockContinue(screen, ReadContext());
            if (blocked)
                ContinueBlocks++;
            return blocked;
        }
        catch (Exception exception)
        {
            _core.FailOpenUnexpected(exception);
            return false;
        }
    }

    /// <summary>The summary's cards began to display: the reveal the capture waits on started.</summary>
    public void ObserveRevealStarted(EndOfRunSummaryController summary)
    {
        if (_disposed || summary == null)
            return;
        try
        {
            var screen = summary.StateMachine;
            if (screen != null && screen.gameObject.activeInHierarchy)
                _core.ObserveRevealStarted(screen);
        }
        catch (Exception exception)
        {
            _core.FailOpenUnexpected(exception);
        }
    }

    public EndOfRunScreenController? FindActiveScreen()
    {
        var cached = _cachedScreen;
        if (cached != null)
        {
            if (IsScreenActive(cached))
                return cached;
            _cachedScreen = null;
        }
        if (AppState.CurrentState is not { } state || !DataExtensions.IsEndOfRunState(state))
            return null;

        var now = Time.realtimeSinceStartup;
        if (now < _nextScanAt)
            return null;
        _nextScanAt = now + ScreenScanIntervalSeconds;
        var found = UnityEngine.Object.FindFirstObjectByType<EndOfRunScreenController>();
        if (found != null && IsScreenActive(found))
            _cachedScreen = found;
        return _cachedScreen;
    }

    public bool IsScreenActive(EndOfRunScreenController screen) =>
        screen != null && screen.gameObject != null && screen.gameObject.activeInHierarchy;

    public int GetScreenId(EndOfRunScreenController screen) => screen.GetInstanceID();

    public EndOfRunCaptureReadinessOutcome GetReadiness(
        EndOfRunScreenController screen,
        bool hasRevealStarted
    )
    {
        try
        {
            return ReadReadiness(screen, hasRevealStarted);
        }
        catch (Exception exception)
        {
            return new(
                hasRevealStarted
                    ? EndOfRunCaptureReadinessState.DetectionFailed
                    : EndOfRunCaptureReadinessState.RevealNotStarted,
                ScreenshotCaptureReasonCode.RevealProbeFailed,
                exception
            );
        }
    }

    public IEndOfRunCaptureAttempt BeginCapture(
        EndOfRunScreenController screen,
        ScreenshotCaptureRequest request
    )
    {
        Attempts++;
        return new CaptureAttempt(this, request);
    }

    // No touch blocker: the Continue patch is the only gate, so this only logs the edges.
    public void SetContinueBlocked(EndOfRunScreenController? screen, bool blocked)
    {
        if (blocked == _continueBlocked)
            return;
        _continueBlocked = blocked;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.end_of_run.continue_held"),
            ("held", blocked)
        );
    }

    public BppLogField[] Summary() =>
        [
            ("end_of_run_attempts", Attempts),
            ("end_of_run_frames", FramesRead),
            ("end_of_run_continue_blocks", ContinueBlocks),
        ];

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _core.DetachSurface(this);
        _runInitialized.Dispose();
        Events.RunStarted.RemoveListener(_runStarted);
        Events.EndOfRunScreenInitializing.RemoveListener(_endOfRunInitializing);
        _cachedScreen = null;
    }

    private EndOfRunCaptureReadinessOutcome ReadReadiness(
        EndOfRunScreenController screen,
        bool hasRevealStarted
    )
    {
        var active = screen._activeController;
        var summary = active?.TryCast<EndOfRunSummaryController>();
        if (summary == null)
        {
            ResetStability();
            return new(EndOfRunCaptureReadinessState.NotSummary, null, null);
        }
        if (screen._transitionCount > 0)
            return new(EndOfRunCaptureReadinessState.TransitionInProgress, null, null);
        if (!hasRevealStarted)
        {
            ResetStability();
            return new(EndOfRunCaptureReadinessState.RevealNotStarted, null, null);
        }

        var cards = summary.loadedCards;
        if (cards == null)
            return new(
                EndOfRunCaptureReadinessState.DetectionFailed,
                ScreenshotCaptureReasonCode.RevealProbeFailed,
                null
            );
        var settled = ObserveStability(summary, cards);
        var count = 0;
        var allFaceUp = true;
        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            if (card == null)
                continue;
            count++;
            var animator = card.Animator;
            if (animator == null || !animator.GetBool(FaceUpHash))
                allFaceUp = false;
        }

        // DisplaySkills follows DisplayCardsAsync; until its sequence exists the summary has
        // not reached a settled frame.
        var skills = summary._skillSequence;
        if (skills == null)
            return new(EndOfRunCaptureReadinessState.RevealInProgress, null, null);
        if (skills.duration > 0f && !skills.isComplete)
            return new(EndOfRunCaptureReadinessState.RevealInProgress, null, null);

        if (count == 0 || allFaceUp || settled)
            return new(EndOfRunCaptureReadinessState.Ready, null, null);
        // The cards cannot prove they settled (FaceUp stays unset here, see the class summary):
        // the deadline then captures what the screen shows instead of nothing.
        return new(EndOfRunCaptureReadinessState.DetectionFailed, null, null);
    }

    // The desktop's structural rule over each card's animator subtree (local poses, VFX and
    // particle subtrees left out): a pose that moved and then held for 500 ms. The game's raw
    // reveal never sets FaceUp, and the reveal can finish before the first sample, so a pose that
    // held from the first sample for StillSeconds also counts. The node list is built once per
    // summary; each sample, at the shared cadence, reads only the local poses through interop.
    private bool ObserveStability(
        EndOfRunSummaryController summary,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2Cpp.ItemController> cards
    )
    {
        var summaryId = summary.GetInstanceID();
        if (_stabilitySummaryId != summaryId || _poseCardCount != CountCards(cards))
        {
            ResetStability();
            _stabilitySummaryId = summaryId;
            BuildPoseNodes(cards);
        }
        var now = Time.realtimeSinceStartup;
        if (!_cadence.ShouldSample(now, summaryId))
            return _stability.ObserveCached() || now - _lastPoseChangeAt >= StillSeconds;

        ulong pose = HashOffset;
        foreach (var node in _poseNodes)
        {
            if (node == null)
                continue;
            var position = node.localPosition;
            var rotation = node.localRotation;
            var scale = node.localScale;
            pose = Mix(pose, Quantize(position.x));
            pose = Mix(pose, Quantize(position.y));
            pose = Mix(pose, Quantize(position.z));
            pose = Mix(pose, Quantize(rotation.x));
            pose = Mix(pose, Quantize(rotation.y));
            pose = Mix(pose, Quantize(rotation.z));
            pose = Mix(pose, Quantize(rotation.w));
            pose = Mix(pose, Quantize(scale.x));
            pose = Mix(pose, Quantize(scale.y));
            pose = Mix(pose, Quantize(scale.z));
        }
        if (!_hasPose || pose != _lastPose)
        {
            _hasPose = true;
            _lastPose = pose;
            _lastPoseChangeAt = now;
        }
        var moved = _stability.Observe(_poseCardCount, _poseSet, pose, now);
        return moved || now - _lastPoseChangeAt >= StillSeconds;
    }

    private void BuildPoseNodes(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2Cpp.ItemController> cards
    )
    {
        _poseNodes.Clear();
        _poseSet = HashOffset;
        _poseCardCount = 0;
        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            var animator = card == null ? null : card.Animator;
            if (animator == null)
                continue;
            _poseCardCount++;
            _poseSet = Mix(_poseSet, (ulong)(uint)animator.GetInstanceID());
            AddSubtree(animator.transform);
        }
    }

    private void AddSubtree(Transform transform)
    {
        _poseNodes.Add(transform);
        _poseSet = Mix(_poseSet, (ulong)(uint)transform.GetInstanceID());
        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            if (child == null || IsEffect(child))
                continue;
            AddSubtree(child);
        }
    }

    // Particles and VFX move forever; they are not the cards' pose (the desktop's exclusion).
    private static bool IsEffect(Transform transform)
    {
        var name = transform.name ?? "";
        return name.Contains("vfx", StringComparison.OrdinalIgnoreCase)
            || name.Contains("particle", StringComparison.OrdinalIgnoreCase)
            || transform.GetComponent<ParticleSystem>() != null;
    }

    private static int CountCards(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2Cpp.ItemController> cards
    )
    {
        var count = 0;
        for (var index = 0; index < cards.Length; index++)
            if (cards[index] is { } card && card.Animator != null)
                count++;
        return count;
    }

    private static ulong Quantize(float value) => (ulong)(long)Math.Round(value * 1000f);

    private static ulong Mix(ulong hash, ulong value) => (hash ^ value) * 1099511628211UL;

    private void ResetStability()
    {
        _stability.Reset();
        _cadence.Reset();
        _stabilitySummaryId = 0;
        _poseNodes.Clear();
        _poseCardCount = -1;
        _poseSet = HashOffset;
        _hasPose = false;
        _lastPose = 0;
        _lastPoseChangeAt = 0f;
    }

    private EndOfRunCaptureContext ReadContext()
    {
        RefreshRunId();
        return new EndOfRunCaptureContext(_enabled(), _bufferedRunId, HeroName: null);
    }

    private void RefreshRunId()
    {
        var runId = _services.RunContext.CurrentServerRunId;
        if (!string.IsNullOrWhiteSpace(runId))
            _bufferedRunId = runId;
    }

    private void OnRunStarted()
    {
        if (_disposed)
            return;
        _cachedScreen = null;
        _nextScanAt = 0f;
        ResetStability();
        _bufferedRunId = null;
        _core.OnRunStarted();
        RefreshRunId();
    }

    private void OnEndOfRunInitializing()
    {
        if (_disposed)
            return;
        _cachedScreen = null;
        _nextScanAt = 0f;
        ResetStability();
        _core.OnEndOfRunInitializing();
    }

    private static Il2CppSystem.Action Convert(Action handler) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(handler)
        ?? throw new InvalidOperationException("Il2CppInterop returned no delegate.");

    private ScreenshotCaptureResult NewResult(ScreenshotCaptureRequest request)
    {
        var capturedAtLocal = DateTimeOffset.Now;
        var relativePath = ScreenshotPathBuilder.BuildRelativePath(request.RunId, capturedAtLocal);
        return new ScreenshotCaptureResult
        {
            // Copied before the frame: persistence never re-reads the live run.
            Metadata = ScreenshotCaptureMetadata.Capture(_services.RunSnapshot),
            ScreenshotId = request.ScreenshotId,
            RunId = request.RunId,
            HeroName = request.HeroName,
            RelativePath = relativePath,
            FilePath = Path.Combine(_screenshotDirectory, relativePath),
            CapturedAtLocal = capturedAtLocal,
            CapturedAtUtc = capturedAtLocal.ToUniversalTime(),
        };
    }

    private static ScreenshotCaptureReasonCode? HideTooltip()
    {
        try
        {
            UnityEngine
                .Object.FindFirstObjectByType<TooltipParentComponent>()
                ?.HideCardTooltipController();
            return null;
        }
        catch
        {
            return ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable;
        }
    }

    private sealed class UnityClock : IEndOfRunCaptureClock
    {
        public float UnscaledSeconds => Time.unscaledTime;
        public float RealtimeSeconds => Time.realtimeSinceStartup;
        public long Milliseconds => (long)Math.Round(Time.realtimeSinceStartup * 1000d);
    }

    /// <summary>
    /// One capture: a frame for the tooltip to close, two more for the screen to redraw without
    /// it, then the end-of-frame read. Continue is released once the pixels are managed bytes;
    /// the PNG is written on a pool thread.
    /// </summary>
    private sealed class CaptureAttempt : IEndOfRunCaptureAttempt
    {
        private readonly object _gate = new();
        private readonly AndroidEndOfRunCapture _owner;
        private readonly ScreenshotCaptureRequest _request;
        private readonly TaskCompletionSource<bool> _frameAcquired = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<EndOfRunCaptureAttemptOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private ScreenshotCaptureReasonCode? _degradation;
        private bool _canceled;
        private bool _started;

        public CaptureAttempt(AndroidEndOfRunCapture owner, ScreenshotCaptureRequest request)
        {
            _owner = owner;
            _request = request;
            MelonCoroutines.Start(Run());
        }

        public bool HasCaptureStarted
        {
            get
            {
                lock (_gate)
                    return _started;
            }
        }

        public Task FrameAcquired => _frameAcquired.Task;

        public Task<EndOfRunCaptureAttemptOutcome> Completion => _completion.Task;

        public void RestoreUi() { }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_canceled)
                    return;
                _canceled = true;
                if (_started)
                    return;
            }
            CompleteCanceled();
        }

        private bool IsCanceled()
        {
            lock (_gate)
                return _canceled;
        }

        // From here a cancel lets the attempt finish and report, as on the desktop.
        private bool TryStart()
        {
            lock (_gate)
            {
                if (_canceled)
                    return false;
                _started = true;
                return true;
            }
        }

        private IEnumerator Run()
        {
            yield return null;
            if (IsCanceled())
                yield break;
            _degradation = HideTooltip();
            yield return null;
            yield return null;
            if (!TryStart())
                yield break;
            ScreenshotCaptureResult result;
            try
            {
                result = _owner.NewResult(_request);
            }
            catch (Exception exception)
            {
                Fail(ScreenshotCaptureReasonCode.CaptureSynchronousException, exception);
                yield break;
            }

            yield return new WaitForEndOfFrame();
            AndroidScreenCapture.Frame frame;
            try
            {
                frame = AndroidScreenCapture.ReadScreen();
            }
            catch (Exception exception)
            {
                Fail(ScreenshotCaptureReasonCode.CaptureSynchronousException, exception);
                yield break;
            }
            _owner.FramesRead++;
            _frameAcquired.TrySetResult(true);
            _ = Task.Run(() =>
            {
                try
                {
                    // Texture rows run bottom-up; PNG rows top-down.
                    ScreenshotPngWriter.FlipRowsVertically(
                        frame.Rgba,
                        frame.Width * 4,
                        frame.Height
                    );
                    ScreenshotPngWriter.WriteOpaquePngAtomically(
                        result.FilePath,
                        frame.Rgba,
                        frame.Width,
                        frame.Height
                    );
                    _completion.TrySetResult(
                        EndOfRunCaptureAttemptOutcome.Succeeded(result, _degradation)
                    );
                }
                catch (Exception exception)
                {
                    _completion.TrySetResult(
                        EndOfRunCaptureAttemptOutcome.Failed(
                            ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                            exception
                        )
                    );
                }
            });
        }

        private void Fail(ScreenshotCaptureReasonCode reason, Exception exception)
        {
            _frameAcquired.TrySetException(exception);
            _completion.TrySetResult(EndOfRunCaptureAttemptOutcome.Failed(reason, exception));
        }

        private void CompleteCanceled()
        {
            _frameAcquired.TrySetCanceled();
            _completion.TrySetResult(
                EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.ContextExpired)
            );
        }
    }
}
