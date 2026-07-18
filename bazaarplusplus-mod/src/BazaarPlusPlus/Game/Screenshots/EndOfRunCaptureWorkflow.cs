#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Game.Screenshots;

internal interface IEndOfRunCaptureWorkflow
{
    bool ShouldBlockContinue(EndOfRunScreenController screen);
    void ObserveRevealStarted(EndOfRunSummaryController summary);
}

internal enum EndOfRunCaptureReadinessState
{
    UnknownTarget,
    NotSummary,
    TransitionInProgress,
    RevealNotStarted,
    RevealInProgress,
    Ready,
    DetectionFailed,
}

internal readonly record struct EndOfRunCaptureReadinessOutcome(
    EndOfRunCaptureReadinessState State,
    ScreenshotCaptureReasonCode? ReasonCode,
    Exception? Exception
);

internal sealed class EndOfRunCaptureWorkflow : IEndOfRunCaptureWorkflow, IDisposable
{
    private const string TransitionCountFieldName = "_transitionCount";
    private readonly IBppServices _services;
    private readonly EndOfRunCaptureWorkflowCore<EndOfRunScreenController> _core;
    private EndOfRunCaptureDriver? _driver;
    private string? _bufferedRunId;
    private string? _bufferedHeroName;
    private bool _disposed;

    internal EndOfRunCaptureWorkflow(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _core = new EndOfRunCaptureWorkflowCore<EndOfRunScreenController>(
            new EndOfRunArtifactPersistence(services),
            new UnityEndOfRunCaptureClock(),
            new SystemEndOfRunCaptureFileSystem(),
            EndOfRunCapturePolicy.Default
        );
        RefreshBufferedRunContext();
    }

    public bool ShouldBlockContinue(EndOfRunScreenController screen)
    {
        if (_disposed || screen == null || _driver == null)
            return false;

        try
        {
            return _core.ShouldBlockContinue(screen, ReadContext());
        }
        catch (Exception ex)
        {
            _core.FailOpenUnexpected(ex);
            return false;
        }
    }

    public void ObserveRevealStarted(EndOfRunSummaryController summary)
    {
        if (_disposed || summary == null || _driver == null)
            return;

        try
        {
            var screen = summary.StateMachine;
            if (screen != null && screen.gameObject.activeInHierarchy)
                _core.ObserveRevealStarted(screen);
        }
        catch (Exception ex)
        {
            _core.FailOpenUnexpected(ex);
        }
    }

    internal void AttachDriver(EndOfRunCaptureDriver driver)
    {
        if (_disposed)
            return;
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _core.AttachSurface(driver);
    }

    internal void DetachDriver(EndOfRunCaptureDriver driver)
    {
        if (!ReferenceEquals(_driver, driver))
            return;
        _core.DetachSurface(driver);
        _driver = null;
    }

    internal void OnFrame()
    {
        if (_disposed)
            return;
        try
        {
            _core.OnFrame(ReadContext());
        }
        catch (Exception ex)
        {
            _core.FailOpenUnexpected(ex);
        }
    }

    internal void OnRunStarted()
    {
        if (_disposed)
            return;
        _core.OnRunStarted();
        _bufferedRunId = null;
        _bufferedHeroName = null;
        RefreshBufferedRunContext();
    }

    internal void OnEndOfRunInitializing()
    {
        if (!_disposed)
            _core.OnEndOfRunInitializing();
    }

    internal void ObserveRunInitialized(RunInitializedObserved observed)
    {
        if (!string.IsNullOrWhiteSpace(observed.RunId))
            _bufferedRunId = observed.RunId;
        RefreshBufferedRunContext();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_driver != null)
            _core.DetachSurface(_driver);
        _driver = null;
    }

    internal static EndOfRunCaptureReadinessOutcome ReadReadiness(
        object? screen,
        bool hasSummaryRevealStarted
    )
    {
        if (screen == null)
            return Outcome(EndOfRunCaptureReadinessState.NotSummary);

        try
        {
            var reveal = EndOfRunSummaryRevealDetector.GetRevealOutcome(screen);
            if (reveal.State == EndOfRunSummaryRevealState.TargetDetectionFailed)
            {
                return Outcome(
                    EndOfRunCaptureReadinessState.UnknownTarget,
                    reveal.ReasonCode,
                    reveal.Exception
                );
            }
            if (reveal.State == EndOfRunSummaryRevealState.NotSummary)
                return Outcome(EndOfRunCaptureReadinessState.NotSummary);

            if (!TryGetTransitionCount(screen, out var transitionCount))
            {
                return Outcome(
                    hasSummaryRevealStarted
                        ? EndOfRunCaptureReadinessState.DetectionFailed
                        : EndOfRunCaptureReadinessState.RevealNotStarted,
                    ScreenshotCaptureReasonCode.TransitionFieldMissing
                );
            }
            if (transitionCount > 0)
                return Outcome(EndOfRunCaptureReadinessState.TransitionInProgress);
            if (!hasSummaryRevealStarted)
                return Outcome(EndOfRunCaptureReadinessState.RevealNotStarted);

            var state = reveal.State switch
            {
                EndOfRunSummaryRevealState.NoLoadedCards => EndOfRunCaptureReadinessState.Ready,
                EndOfRunSummaryRevealState.RevealInProgress =>
                    EndOfRunCaptureReadinessState.RevealInProgress,
                EndOfRunSummaryRevealState.RevealComplete => EndOfRunCaptureReadinessState.Ready,
                _ => EndOfRunCaptureReadinessState.DetectionFailed,
            };
            return Outcome(
                state,
                state == EndOfRunCaptureReadinessState.DetectionFailed
                    ? reveal.ReasonCode ?? ScreenshotCaptureReasonCode.RevealProbeFailed
                    : null,
                reveal.Exception
            );
        }
        catch (Exception ex)
        {
            return Outcome(
                hasSummaryRevealStarted
                    ? EndOfRunCaptureReadinessState.DetectionFailed
                    : EndOfRunCaptureReadinessState.RevealNotStarted,
                ScreenshotCaptureReasonCode.RevealProbeFailed,
                ex
            );
        }
    }

    private EndOfRunCaptureContext ReadContext()
    {
        RefreshBufferedRunContext();
        return new EndOfRunCaptureContext(
            _driver?.IsAvailable == true
                && EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(_services.Config),
            _bufferedRunId,
            _bufferedHeroName
        );
    }

    private void RefreshBufferedRunContext()
    {
        var runId = _services.RunContext.CurrentServerRunId;
        if (!string.IsNullOrWhiteSpace(runId))
            _bufferedRunId = runId;

        var heroName = Data.Run?.Player?.Hero.ToString();
        if (!string.IsNullOrWhiteSpace(heroName))
            _bufferedHeroName = heroName;
    }

    private static bool TryGetTransitionCount(object screen, out int transitionCount)
    {
        transitionCount = 0;
        var field = screen
            .GetType()
            .GetField(
                TransitionCountFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (field == null)
            return false;

        transitionCount = (int?)field.GetValue(screen) ?? 0;
        return true;
    }

    private static EndOfRunCaptureReadinessOutcome Outcome(
        EndOfRunCaptureReadinessState state,
        ScreenshotCaptureReasonCode? reasonCode = null,
        Exception? exception = null
    ) => new(state, reasonCode, exception);
}
