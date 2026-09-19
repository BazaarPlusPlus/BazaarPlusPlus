#nullable enable
#if DEBUG
using System.Diagnostics;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunCaptureSamplingDiagnostics
{
    private int _readinessSampleCount;
    private long _readinessTotalMicroseconds;
    private long _readinessMaxMicroseconds;
    private int _barrierSampleCount;
    private long _barrierTotalMicroseconds;
    private long _barrierMaxMicroseconds;
    private int _maxCardCount;
    private int _maxTransformCount;
    private int _maxControllerCount;
    private int _maxSkippedInactiveControllerCount;
    private NativeTooltipCleanFrameReasonCode _nativeTooltipReasonCode;

    [Conditional("DEBUG")]
    internal void RecordReadiness(long startedAt, EndOfRunSummaryVisualSnapshot snapshot)
    {
        var elapsed = ElapsedMicroseconds(startedAt);
        _readinessSampleCount++;
        _readinessTotalMicroseconds += elapsed;
        _readinessMaxMicroseconds = Math.Max(_readinessMaxMicroseconds, elapsed);
        RecordVisualCounts(snapshot.LoadedCardCount, snapshot.TransformCount);
    }

    [Conditional("DEBUG")]
    internal void RecordBarrier(
        long startedAt,
        NativeTooltipCleanFrameAudit tooltipAudit,
        EndOfRunCleanFrameVisualObservation visual
    )
    {
        var elapsed = ElapsedMicroseconds(startedAt);
        _barrierSampleCount++;
        _barrierTotalMicroseconds += elapsed;
        _barrierMaxMicroseconds = Math.Max(_barrierMaxMicroseconds, elapsed);
        _maxControllerCount = Math.Max(_maxControllerCount, tooltipAudit.ControllerCount);
        _maxSkippedInactiveControllerCount = Math.Max(
            _maxSkippedInactiveControllerCount,
            tooltipAudit.SkippedInactiveControllerCount
        );
        if (
            _nativeTooltipReasonCode == NativeTooltipCleanFrameReasonCode.None
            && tooltipAudit.ReasonCode != NativeTooltipCleanFrameReasonCode.None
        )
        {
            _nativeTooltipReasonCode = tooltipAudit.ReasonCode;
        }
        RecordVisualCounts(visual.LoadedCardCount, visual.TransformCount);
    }

    [Conditional("DEBUG")]
    internal void ReportAndReset()
    {
        BppLog.DebugEvent(
            ScreenshotCaptureLogEvents.SamplingSummary,
            () =>
                [
                    ScreenshotCaptureLogEvents.SamplingReadinessCount.Bind(_readinessSampleCount),
                    ScreenshotCaptureLogEvents.SamplingReadinessTotalMicroseconds.Bind(
                        _readinessTotalMicroseconds
                    ),
                    ScreenshotCaptureLogEvents.SamplingReadinessMaxMicroseconds.Bind(
                        _readinessMaxMicroseconds
                    ),
                    ScreenshotCaptureLogEvents.SamplingBarrierCount.Bind(_barrierSampleCount),
                    ScreenshotCaptureLogEvents.SamplingBarrierTotalMicroseconds.Bind(
                        _barrierTotalMicroseconds
                    ),
                    ScreenshotCaptureLogEvents.SamplingBarrierMaxMicroseconds.Bind(
                        _barrierMaxMicroseconds
                    ),
                    ScreenshotCaptureLogEvents.SamplingMaxCardCount.Bind(_maxCardCount),
                    ScreenshotCaptureLogEvents.SamplingMaxTransformCount.Bind(_maxTransformCount),
                    ScreenshotCaptureLogEvents.SamplingMaxControllerCount.Bind(_maxControllerCount),
                    ScreenshotCaptureLogEvents.SamplingMaxSkippedInactiveControllerCount.Bind(
                        _maxSkippedInactiveControllerCount
                    ),
                    ScreenshotCaptureLogEvents.SamplingNativeTooltipReasonCode.Bind(
                        _nativeTooltipReasonCode
                    ),
                ]
        );
        Reset();
    }

    [Conditional("DEBUG")]
    internal void Reset()
    {
        _readinessSampleCount = 0;
        _readinessTotalMicroseconds = 0L;
        _readinessMaxMicroseconds = 0L;
        _barrierSampleCount = 0;
        _barrierTotalMicroseconds = 0L;
        _barrierMaxMicroseconds = 0L;
        _maxCardCount = 0;
        _maxTransformCount = 0;
        _maxControllerCount = 0;
        _maxSkippedInactiveControllerCount = 0;
        _nativeTooltipReasonCode = NativeTooltipCleanFrameReasonCode.None;
    }

    private void RecordVisualCounts(int cardCount, int transformCount)
    {
        _maxCardCount = Math.Max(_maxCardCount, cardCount);
        _maxTransformCount = Math.Max(_maxTransformCount, transformCount);
    }

    private static long ElapsedMicroseconds(long startedAt)
    {
        var elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - startedAt);
        return (long)Math.Ceiling(elapsedTicks * 1_000_000d / Stopwatch.Frequency);
    }
}
#endif
