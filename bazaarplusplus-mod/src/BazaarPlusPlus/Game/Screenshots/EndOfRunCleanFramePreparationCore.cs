#nullable enable
using BazaarPlusPlus.GameInterop.Tooltips;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum EndOfRunCleanFrameVisualState
{
    Unavailable,
    Empty,
    Sampled,
}

internal readonly record struct EndOfRunCleanFrameVisualObservation(
    EndOfRunCleanFrameVisualState State,
    int LoadedCardCount,
    ulong CardSetFingerprint,
    ulong PoseFingerprint
)
{
    internal static EndOfRunCleanFrameVisualObservation Unavailable =>
        new(EndOfRunCleanFrameVisualState.Unavailable, 0, 0, 0);

    internal static EndOfRunCleanFrameVisualObservation Empty =>
        new(EndOfRunCleanFrameVisualState.Empty, 0, 0, 0);

    internal static EndOfRunCleanFrameVisualObservation Sampled(
        int loadedCardCount,
        ulong cardSetFingerprint,
        ulong poseFingerprint
    ) =>
        new(
            EndOfRunCleanFrameVisualState.Sampled,
            loadedCardCount,
            cardSetFingerprint,
            poseFingerprint
        );
}

internal enum EndOfRunCleanFrameDecisionKind
{
    Wait,
    Capture,
    Fail,
}

internal readonly record struct EndOfRunCleanFrameDecision(
    EndOfRunCleanFrameDecisionKind Kind,
    ScreenshotCaptureReasonCode? ReasonCode
)
{
    internal static EndOfRunCleanFrameDecision Wait =>
        new(EndOfRunCleanFrameDecisionKind.Wait, null);

    internal static EndOfRunCleanFrameDecision Capture =>
        new(EndOfRunCleanFrameDecisionKind.Capture, null);

    internal static EndOfRunCleanFrameDecision Fail(ScreenshotCaptureReasonCode reasonCode) =>
        new(EndOfRunCleanFrameDecisionKind.Fail, reasonCode);
}

/// <summary>
/// Pure timing owner for the render-frame barrier between native tooltip cleanup and capture.
/// </summary>
internal sealed class EndOfRunCleanFramePreparationCore
{
    internal const float RequiredStableSeconds = 0.5f;
    internal const float DeadlineSeconds = 5f;

    private readonly float _deadlineAtSeconds;
    private bool _hasCleanBaseline;
    private int _loadedCardCount;
    private ulong _cardSetFingerprint;
    private ulong _poseFingerprint;
    private float _stableSinceSeconds;

    internal EndOfRunCleanFramePreparationCore(float startedAtSeconds)
    {
        _deadlineAtSeconds = startedAtSeconds + DeadlineSeconds;
    }

    internal EndOfRunCleanFrameDecision Observe(
        NativeTooltipCleanFrameAudit tooltipAudit,
        EndOfRunCleanFrameVisualObservation visual,
        float nowSeconds
    )
    {
        if (tooltipAudit.State == NativeTooltipCleanFrameState.Unavailable)
        {
            return EndOfRunCleanFrameDecision.Fail(
                ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable
            );
        }
        if (visual.State == EndOfRunCleanFrameVisualState.Unavailable)
        {
            return EndOfRunCleanFrameDecision.Fail(
                ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable
            );
        }
        if (float.IsNaN(nowSeconds) || float.IsInfinity(nowSeconds))
        {
            return EndOfRunCleanFrameDecision.Fail(
                ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable
            );
        }

        if (tooltipAudit.State != NativeTooltipCleanFrameState.Clean)
        {
            ResetBaseline();
            return nowSeconds >= _deadlineAtSeconds
                ? EndOfRunCleanFrameDecision.Fail(ScreenshotCaptureReasonCode.CleanFrameDeadline)
                : EndOfRunCleanFrameDecision.Wait;
        }

        if (nowSeconds >= _deadlineAtSeconds)
            return EndOfRunCleanFrameDecision.Fail(ScreenshotCaptureReasonCode.CleanFrameDeadline);

        if (visual.State == EndOfRunCleanFrameVisualState.Empty)
            return EndOfRunCleanFrameDecision.Capture;

        if (visual.LoadedCardCount <= 0)
        {
            return EndOfRunCleanFrameDecision.Fail(
                ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable
            );
        }

        if (
            !_hasCleanBaseline
            || visual.LoadedCardCount != _loadedCardCount
            || visual.CardSetFingerprint != _cardSetFingerprint
            || nowSeconds < _stableSinceSeconds
        )
        {
            SetBaseline(visual, nowSeconds);
            return EndOfRunCleanFrameDecision.Wait;
        }

        if (visual.PoseFingerprint != _poseFingerprint)
        {
            _poseFingerprint = visual.PoseFingerprint;
            _stableSinceSeconds = nowSeconds;
            return EndOfRunCleanFrameDecision.Wait;
        }

        return nowSeconds - _stableSinceSeconds >= RequiredStableSeconds
            ? EndOfRunCleanFrameDecision.Capture
            : EndOfRunCleanFrameDecision.Wait;
    }

    private void SetBaseline(EndOfRunCleanFrameVisualObservation visual, float nowSeconds)
    {
        _hasCleanBaseline = true;
        _loadedCardCount = visual.LoadedCardCount;
        _cardSetFingerprint = visual.CardSetFingerprint;
        _poseFingerprint = visual.PoseFingerprint;
        _stableSinceSeconds = nowSeconds;
    }

    private void ResetBaseline()
    {
        _hasCleanBaseline = false;
        _loadedCardCount = 0;
        _cardSetFingerprint = 0;
        _poseFingerprint = 0;
        _stableSinceSeconds = 0f;
    }
}
