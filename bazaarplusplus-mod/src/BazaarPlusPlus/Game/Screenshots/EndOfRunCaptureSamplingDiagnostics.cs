#nullable enable
#if DEBUG
using BazaarPlusPlus.Infrastructure.Logging;
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
            new BppLogEvent(BppLogFeatureScope.Screenshots, "screenshots.capture.sampling_summary"),
            () =>
                [
                    ("readiness_sample_count", _readinessSampleCount),
                    ("readiness_total_us", _readinessTotalMicroseconds),
                    ("readiness_max_us", _readinessMaxMicroseconds),
                    ("barrier_sample_count", _barrierSampleCount),
                    ("barrier_total_us", _barrierTotalMicroseconds),
                    ("barrier_max_us", _barrierMaxMicroseconds),
                    ("max_card_count", _maxCardCount),
                    ("max_transform_count", _maxTransformCount),
                    ("max_controller_count", _maxControllerCount),
                    ("max_skipped_inactive_controller_count", _maxSkippedInactiveControllerCount),
                    ("native_tooltip_reason_code", _nativeTooltipReasonCode),
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
