#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.GameInterop.Tooltips;

internal static class EndOfRunCaptureSuppressionLifecycleTests
{
    internal static void Run()
    {
        Unavailable_native_audit_releases_only_the_native_lease();
        Unavailable_native_audit_short_circuits_a_throwing_visual_probe();
        Native_audit_exception_releases_only_the_native_lease();
        Unavailable_visual_keeps_suppression_until_restore();
        Visual_exception_uses_the_visual_reason_and_keeps_suppression_until_restore();
        Cancel_before_capture_restores_each_suppression_once();
        Frame_acquired_restore_is_exactly_once();
        Install_after_cancel_restores_new_resources_without_reopening_the_lifecycle();
    }

    private static void Unavailable_native_audit_short_circuits_a_throwing_visual_probe()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Unavailable);
        var lifecycle = Installed(bpp, native);
        var visualProbeCalled = false;

        var decision = lifecycle.ObserveCleanFrame(
            new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f),
            () =>
            {
                visualProbeCalled = true;
                throw new InvalidOperationException("visual audit failed");
            },
            nowSeconds: 0f
        );

        AssertNativeDegradation(
            decision,
            "Native unavailability must retain priority over a later visual failure."
        );
        Assert(!visualProbeCalled, "A terminal native audit must skip the visual probe.");
        Assert(
            native.DisposeCount == 1,
            "The unavailable native lease must release before capture."
        );
        Assert(bpp.DisposeCount == 0, "BPP chrome must stay suppressed until frame acquisition.");
        lifecycle.ReleaseAll();
        Assert(bpp.DisposeCount == 1, "Final restore must release BPP chrome.");
        Assert(
            native.DisposeCount == 1,
            "Final restore must not release native suppression twice."
        );
    }

    private static void Unavailable_native_audit_releases_only_the_native_lease()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Unavailable);
        var lifecycle = Installed(bpp, native);

        var decision = lifecycle.ObserveCleanFrame(
            new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f),
            Sample,
            nowSeconds: 0f
        );

        AssertNativeDegradation(decision, "An unavailable native audit must degrade capture.");
        Assert(
            native.DisposeCount == 1,
            "The unavailable native lease must release before capture."
        );
        Assert(bpp.DisposeCount == 0, "BPP chrome must stay suppressed until frame acquisition.");

        lifecycle.ReleaseAll();
        Assert(bpp.DisposeCount == 1, "Final restore must release BPP chrome.");
        Assert(
            native.DisposeCount == 1,
            "Final restore must not release native suppression twice."
        );
    }

    private static void Native_audit_exception_releases_only_the_native_lease()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(new InvalidOperationException("native audit failed"));
        var lifecycle = Installed(bpp, native);
        var observedReason = NativeTooltipCleanFrameReasonCode.None;

        var decision = lifecycle.ObserveCleanFrame(
            new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f),
            Sample,
            nowSeconds: 0f,
            (tooltipAudit, _) => observedReason = tooltipAudit.ReasonCode
        );

        AssertNativeDegradation(decision, "A native audit exception must use the native reason.");
        Assert(
            observedReason == NativeTooltipCleanFrameReasonCode.AuditException,
            "Diagnostics must retain the native audit exception reason."
        );
        Assert(native.DisposeCount == 1, "A throwing native lease must release before capture.");
        Assert(bpp.DisposeCount == 0, "Native failure must not release reliable BPP suppression.");
        lifecycle.ReleaseAll();
        Assert(
            bpp.DisposeCount == 1,
            "Final restore must release BPP chrome after native failure."
        );
    }

    private static void Unavailable_visual_keeps_suppression_until_restore()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Clean);
        var lifecycle = Installed(bpp, native);

        var decision = lifecycle.ObserveCleanFrame(
            new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f),
            () => EndOfRunCleanFrameVisualObservation.Unavailable,
            nowSeconds: 0f
        );

        AssertVisualDegradation(decision, "An unavailable visual must use the visual reason.");
        Assert(
            native.DisposeCount == 0,
            "A valid native gate must remain held for degraded capture."
        );
        Assert(bpp.DisposeCount == 0, "BPP chrome must remain held for degraded capture.");
        lifecycle.ReleaseAll();
        Assert(native.DisposeCount == 1, "Restore must release native suppression.");
        Assert(bpp.DisposeCount == 1, "Restore must release BPP chrome.");
    }

    private static void Visual_exception_uses_the_visual_reason_and_keeps_suppression_until_restore()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Clean);
        var lifecycle = Installed(bpp, native);

        var decision = lifecycle.ObserveCleanFrame(
            new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f),
            () => throw new InvalidOperationException("visual audit failed"),
            nowSeconds: 0f
        );

        AssertVisualDegradation(decision, "A visual exception must not be reported as native.");
        Assert(
            native.DisposeCount == 0,
            "Visual failure must not discard reliable native suppression."
        );
        Assert(bpp.DisposeCount == 0, "Visual failure must not discard BPP suppression.");
        lifecycle.ReleaseAll();
        Assert(
            native.DisposeCount == 1,
            "Restore must release native suppression after visual failure."
        );
        Assert(bpp.DisposeCount == 1, "Restore must release BPP chrome after visual failure.");
    }

    private static void Cancel_before_capture_restores_each_suppression_once()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Clean);
        var lifecycle = Installed(bpp, native);

        lifecycle.ReleaseAll();
        lifecycle.ReleaseAll();

        Assert(native.DisposeCount == 1, "Cancel must restore native suppression exactly once.");
        Assert(bpp.DisposeCount == 1, "Cancel must restore BPP suppression exactly once.");
    }

    private static void Frame_acquired_restore_is_exactly_once()
    {
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Clean);
        var lifecycle = Installed(bpp, native);

        lifecycle.ReleaseAll();
        lifecycle.ReleaseAll();

        Assert(native.DisposeCount == 1, "Frame acquisition must restore native suppression once.");
        Assert(bpp.DisposeCount == 1, "Frame acquisition must restore BPP suppression once.");
    }

    private static void Install_after_cancel_restores_new_resources_without_reopening_the_lifecycle()
    {
        var lifecycle = new EndOfRunCaptureSuppressionLifecycle();
        lifecycle.ReleaseAll();
        var bpp = new CountingDisposable();
        var native = new NativeLease(NativeTooltipCleanFrameState.Clean);

        var installed = lifecycle.TryInstall(bpp, native);

        Assert(!installed, "A canceled lifecycle must not accept late suppression resources.");
        Assert(native.DisposeCount == 1, "Late native suppression must be restored immediately.");
        Assert(bpp.DisposeCount == 1, "Late BPP suppression must be restored immediately.");
    }

    private static EndOfRunCaptureSuppressionLifecycle Installed(
        CountingDisposable bpp,
        NativeLease native
    )
    {
        var lifecycle = new EndOfRunCaptureSuppressionLifecycle();
        Assert(
            lifecycle.TryInstall(bpp, native),
            "Suppression should install on an open lifecycle."
        );
        return lifecycle;
    }

    private static EndOfRunCleanFrameVisualObservation Sample() =>
        EndOfRunCleanFrameVisualObservation.Sampled(1, 1, 1);

    private static void AssertNativeDegradation(
        EndOfRunCleanFrameDecision decision,
        string message
    ) =>
        Assert(
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && decision.ReasonCode
                    == ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable,
            message
        );

    private static void AssertVisualDegradation(
        EndOfRunCleanFrameDecision decision,
        string message
    ) =>
        Assert(
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && decision.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable,
            message
        );

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CountingDisposable : IDisposable
    {
        internal int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class NativeLease : INativeTooltipSuppressionLease
    {
        private readonly NativeTooltipCleanFrameState _state;
        private readonly Exception? _exception;

        internal NativeLease(NativeTooltipCleanFrameState state)
        {
            _state = state;
        }

        internal NativeLease(Exception exception)
        {
            _exception = exception;
        }

        internal int DisposeCount { get; private set; }

        public NativeTooltipCleanFrameAudit AuditCleanFrame()
        {
            if (_exception != null)
                throw _exception;
            return new NativeTooltipCleanFrameAudit(_state);
        }

        public void Dispose() => DisposeCount++;
    }
}
