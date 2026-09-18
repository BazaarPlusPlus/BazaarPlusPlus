#nullable enable
using BazaarPlusPlus.GameInterop.Tooltips;

namespace BazaarPlusPlus.Game.Screenshots;

/// <summary>
/// Owns the independent native-tooltip and BPP-chrome suppression lifetimes for one capture.
/// </summary>
internal sealed class EndOfRunCaptureSuppressionLifecycle
{
    private readonly object _gate = new();
    private IDisposable? _bppSuppression;
    private INativeTooltipSuppressionLease? _nativeTooltipSuppression;
    private bool _closed;

    internal bool TryInstall(
        IDisposable? bppSuppression,
        INativeTooltipSuppressionLease nativeTooltipSuppression
    )
    {
        if (nativeTooltipSuppression == null)
            throw new ArgumentNullException(nameof(nativeTooltipSuppression));

        var alreadyInstalled = false;
        lock (_gate)
        {
            if (!_closed && _nativeTooltipSuppression == null)
            {
                _bppSuppression = bppSuppression;
                _nativeTooltipSuppression = nativeTooltipSuppression;
                return true;
            }
            alreadyInstalled = !_closed;
        }

        DisposeNative(nativeTooltipSuppression);
        DisposeBpp(bppSuppression);
        if (alreadyInstalled)
            throw new InvalidOperationException("Capture suppression is already installed.");
        return false;
    }

    internal EndOfRunCleanFrameDecision ObserveCleanFrame(
        EndOfRunCleanFramePreparationCore preparation,
        Func<EndOfRunCleanFrameVisualObservation> captureVisual,
        float nowSeconds,
        Action<NativeTooltipCleanFrameAudit, EndOfRunCleanFrameVisualObservation>? observeSample =
            null
    )
    {
        if (preparation == null)
            throw new ArgumentNullException(nameof(preparation));
        if (captureVisual == null)
            throw new ArgumentNullException(nameof(captureVisual));

        INativeTooltipSuppressionLease? nativeTooltipSuppression;
        lock (_gate)
            nativeTooltipSuppression = _nativeTooltipSuppression;

        NativeTooltipCleanFrameAudit tooltipAudit;
        try
        {
            tooltipAudit =
                nativeTooltipSuppression?.AuditCleanFrame()
                ?? new NativeTooltipCleanFrameAudit(
                    NativeTooltipCleanFrameState.Unavailable,
                    ReasonCode: NativeTooltipCleanFrameReasonCode.SuppressionInactive
                );
        }
        catch
        {
            tooltipAudit = new NativeTooltipCleanFrameAudit(
                NativeTooltipCleanFrameState.Unavailable,
                ReasonCode: NativeTooltipCleanFrameReasonCode.AuditException
            );
            var unavailableVisual = EndOfRunCleanFrameVisualObservation.Unavailable;
            TryObserveSample(observeSample, tooltipAudit, unavailableVisual);
            ReleaseNative();
            return preparation.Observe(tooltipAudit, unavailableVisual, nowSeconds);
        }
        if (tooltipAudit.State == NativeTooltipCleanFrameState.Unavailable)
        {
            var unavailableVisual = EndOfRunCleanFrameVisualObservation.Unavailable;
            TryObserveSample(observeSample, tooltipAudit, unavailableVisual);
            ReleaseNative();
            return preparation.Observe(tooltipAudit, unavailableVisual, nowSeconds);
        }

        EndOfRunCleanFrameVisualObservation visual;
        try
        {
            visual = captureVisual();
        }
        catch
        {
            visual = EndOfRunCleanFrameVisualObservation.Unavailable;
        }

        TryObserveSample(observeSample, tooltipAudit, visual);

        var decision = preparation.Observe(tooltipAudit, visual, nowSeconds);
        if (
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture
            && decision.ReasonCode
                == ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable
        )
        {
            ReleaseNative();
        }
        return decision;
    }

    private static void TryObserveSample(
        Action<NativeTooltipCleanFrameAudit, EndOfRunCleanFrameVisualObservation>? observer,
        NativeTooltipCleanFrameAudit tooltipAudit,
        EndOfRunCleanFrameVisualObservation visual
    )
    {
        try
        {
            observer?.Invoke(tooltipAudit, visual);
        }
        catch
        {
            // Debug diagnostics cannot change clean-frame availability or suppression lifetime.
        }
    }

    internal void ReleaseAll()
    {
        IDisposable? bppSuppression;
        INativeTooltipSuppressionLease? nativeTooltipSuppression;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            bppSuppression = _bppSuppression;
            nativeTooltipSuppression = _nativeTooltipSuppression;
            _bppSuppression = null;
            _nativeTooltipSuppression = null;
        }

        DisposeNative(nativeTooltipSuppression);
        DisposeBpp(bppSuppression);
    }

    private void ReleaseNative()
    {
        INativeTooltipSuppressionLease? nativeTooltipSuppression;
        lock (_gate)
        {
            nativeTooltipSuppression = _nativeTooltipSuppression;
            _nativeTooltipSuppression = null;
        }
        DisposeNative(nativeTooltipSuppression);
    }

    private static void DisposeNative(INativeTooltipSuppressionLease? suppression)
    {
        try
        {
            suppression?.Dispose();
        }
        catch
        {
            // Native teardown is best-effort; BPP chrome still has an independent lifetime.
        }
    }

    private static void DisposeBpp(IDisposable? suppression)
    {
        try
        {
            suppression?.Dispose();
        }
        catch
        {
            // The workflow owns fail-open even when the Unity surface disappeared.
        }
    }
}
