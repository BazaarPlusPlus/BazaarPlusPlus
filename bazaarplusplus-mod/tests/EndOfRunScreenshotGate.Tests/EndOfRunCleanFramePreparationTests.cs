#nullable enable
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.GameInterop.Tooltips;

internal static class EndOfRunCleanFramePreparationTests
{
    internal static void Run()
    {
        Capture_waits_for_a_clean_post_cleanup_pose_window();
        Pose_change_restarts_the_post_cleanup_window();
        Changing_pose_at_deadline_falls_back_to_capture();
        Late_tooltip_visibility_invalidates_the_clean_window();
        Empty_board_can_capture_after_a_clean_render_boundary();
        Unavailable_native_suppression_falls_back_to_capture();
        Dirty_tooltip_at_deadline_falls_back_to_capture();
        Cached_observations_do_not_advance_the_clean_window();
        A_skipped_deadline_frame_requests_one_final_fresh_audit();
        A_cadence_skipped_deadline_still_executes_the_final_audit();
        Unavailable_visual_falls_back_to_capture();
        Invalid_visual_observations_fall_back_to_capture();
        Native_suppression_owners_are_reference_counted_independently();
    }

    private static void Capture_waits_for_a_clean_post_cleanup_pose_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 10f);

        Assert(
            core.Observe(Clean(), Sample(2, 10, 100), nowSeconds: 10f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "The pre-suppression readiness verdict must not authorize capture."
        );
        Assert(
            core.Observe(Clean(), Sample(2, 10, 100), nowSeconds: 10.49f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "Capture must wait for the full post-clean pose window."
        );
        Assert(
            core.Observe(Clean(), Sample(2, 10, 100), nowSeconds: 10.5f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "A clean tooltip audit plus a newly stable card pose should authorize capture."
        );
    }

    private static void Pose_change_restarts_the_post_cleanup_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 11, 100), 0f);
        _ = core.Observe(Clean(), Sample(1, 11, 101), 0.4f);

        Assert(
            core.Observe(Clean(), Sample(1, 11, 101), 0.89f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "Tooltip cleanup card movement must invalidate the old readiness verdict."
        );
        Assert(
            core.Observe(Clean(), Sample(1, 11, 101), 0.91f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "Capture should resume only after the changed pose settles again."
        );
    }

    private static void Changing_pose_at_deadline_falls_back_to_capture()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 11, 100), 0f);
        _ = core.Observe(Clean(), Sample(1, 11, 101), 4.9f);

        var decision = core.Observe(Clean(), Sample(1, 11, 102), 5f);

        Assert(
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && decision.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "An unstable pose at the clean-frame deadline must fall back to capture."
        );
    }

    private static void Late_tooltip_visibility_invalidates_the_clean_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 12, 200), 0f);
        _ = core.Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Dirty),
            Sample(1, 12, 200),
            0.4f
        );

        Assert(
            core.Observe(Clean(), Sample(1, 12, 200), 0.8f).Kind
                == EndOfRunCleanFrameDecisionKind.Wait,
            "A late fade or show must reset the clean window."
        );
        Assert(
            core.Observe(Clean(), Sample(1, 12, 200), 1.31f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "The final clean audit must own a complete fresh pose window."
        );
    }

    private static void Empty_board_can_capture_after_a_clean_render_boundary()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 2f);
        var decision = core.Observe(
            Clean(),
            EndOfRunCleanFrameVisualObservation.Empty,
            nowSeconds: 2f
        );

        Assert(
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture,
            "A clean empty summary should not wait for a nonexistent card pose."
        );

        var expired = core.Observe(
            Clean(),
            EndOfRunCleanFrameVisualObservation.Empty,
            nowSeconds: 7f
        );
        Assert(
            expired.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && expired.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "An expired empty summary should capture with its deadline degradation."
        );
    }

    private static void Unavailable_native_suppression_falls_back_to_capture()
    {
        var unavailable = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f).Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Unavailable),
            Sample(1, 1, 1),
            nowSeconds: 0f
        );
        Assert(
            unavailable.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && unavailable.ReasonCode
                    == ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable,
            "A missing native seam must fall back to ScreenCapture."
        );
    }

    private static void Dirty_tooltip_at_deadline_falls_back_to_capture()
    {
        var deadlineCore = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        var deadline = deadlineCore.Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Dirty),
            Sample(1, 1, 1),
            EndOfRunCleanFramePreparationCore.DeadlineSeconds
        );
        Assert(
            deadline.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && deadline.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "A persistently dirty frame must fall back to capture at the bounded deadline."
        );
    }

    private static void Cached_observations_do_not_advance_the_clean_window()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 1, 1), nowSeconds: 0f);

        Assert(
            core.ObserveCached(nowSeconds: 0.6f).Kind == EndOfRunCleanFrameDecisionKind.Wait,
            "Reusing a clean observation must not advance the fresh-pose stability clock."
        );
        Assert(
            core.Observe(Clean(), Sample(1, 1, 1), nowSeconds: 0.6f).Kind
                == EndOfRunCleanFrameDecisionKind.Capture,
            "A fresh audit may prove the complete clean-frame window."
        );
    }

    private static void A_skipped_deadline_frame_requests_one_final_fresh_audit()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        _ = core.Observe(Clean(), Sample(1, 1, 1), nowSeconds: 4.95f);

        var cachedDecision = core.ObserveCached(
            nowSeconds: EndOfRunCleanFramePreparationCore.DeadlineSeconds
        );
        Assert(
            cachedDecision.Kind == EndOfRunCleanFrameDecisionKind.ObserveFresh,
            "A cadence-skipped deadline frame must request a final native and visual audit."
        );

        var finalDecision = core.Observe(
            new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Dirty),
            Sample(1, 1, 1),
            nowSeconds: EndOfRunCleanFramePreparationCore.DeadlineSeconds
        );
        Assert(
            finalDecision.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && finalDecision.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "The pure core must own the final deadline degradation after the fresh audit."
        );
    }

    private static void A_cadence_skipped_deadline_still_executes_the_final_audit()
    {
        var core = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f);
        var cadence = new EndOfRunHeavySampleCadence();
        var auditCount = 0;

        EndOfRunCleanFrameDecision Tick(float nowSeconds)
        {
            var decision = cadence.ShouldSample(nowSeconds, generation: 1)
                ? ObserveFresh(nowSeconds)
                : core.ObserveCached(nowSeconds);
            return decision.Kind == EndOfRunCleanFrameDecisionKind.ObserveFresh
                ? ObserveFresh(nowSeconds)
                : decision;
        }

        EndOfRunCleanFrameDecision ObserveFresh(float nowSeconds)
        {
            auditCount++;
            return core.Observe(
                new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Dirty),
                Sample(1, 1, 1),
                nowSeconds
            );
        }

        _ = Tick(4.99f);
        var deadlineDecision = Tick(5f);

        Assert(
            auditCount == 2,
            "The deadline must force one final audit even inside the cadence throttle window."
        );
        Assert(
            deadlineDecision.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && deadlineDecision.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameDeadline,
            "The final audit result must return through the pure preparation core."
        );
    }

    private static void Unavailable_visual_falls_back_to_capture()
    {
        var decision = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f).Observe(
            Clean(),
            EndOfRunCleanFrameVisualObservation.Unavailable,
            nowSeconds: 0f
        );

        Assert(
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && decision.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable,
            "An unavailable visual audit must fall back to ScreenCapture."
        );
    }

    private static void Invalid_visual_observations_fall_back_to_capture()
    {
        var invalidTime = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f).Observe(
            Clean(),
            Sample(1, 1, 1),
            nowSeconds: float.NaN
        );
        var invalidCount = new EndOfRunCleanFramePreparationCore(startedAtSeconds: 0f).Observe(
            Clean(),
            Sample(0, 1, 1),
            nowSeconds: 0f
        );

        Assert(
            invalidTime.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && invalidTime.ReasonCode == ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable
                && invalidCount.Kind == EndOfRunCleanFrameDecisionKind.Capture
                && invalidCount.ReasonCode
                    == ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable,
            "Invalid visual observations must degrade capture instead of dropping the artifact."
        );
    }

    private static void Native_suppression_owners_are_reference_counted_independently()
    {
        var ownership = new NativeTooltipSuppressionOwnershipCore();
        ownership.Acquire(NativeTooltipSuppressionOwner.ReplayPresentation);
        ownership.Acquire(NativeTooltipSuppressionOwner.ReplayVideoRecording);
        ownership.Acquire(NativeTooltipSuppressionOwner.EndOfRunCapture);
        ownership.Acquire(NativeTooltipSuppressionOwner.EndOfRunCapture);

        ownership.Release(NativeTooltipSuppressionOwner.ReplayPresentation);
        ownership.Release(NativeTooltipSuppressionOwner.EndOfRunCapture);
        Assert(ownership.IsActive, "One owner must not release another owner's suppression.");
        Assert(
            ownership.LeaseCount(NativeTooltipSuppressionOwner.EndOfRunCapture) == 1,
            "Nested leases for one owner must remain reference counted."
        );

        ownership.Release(NativeTooltipSuppressionOwner.ReplayVideoRecording);
        ownership.Release(NativeTooltipSuppressionOwner.EndOfRunCapture);
        ownership.Release(NativeTooltipSuppressionOwner.EndOfRunCapture);
        Assert(!ownership.IsActive, "Only the final valid lease release should deactivate gates.");
    }

    private static NativeTooltipCleanFrameAudit Clean() => new(NativeTooltipCleanFrameState.Clean);

    private static EndOfRunCleanFrameVisualObservation Sample(
        int loadedCardCount,
        ulong cardSetFingerprint,
        ulong poseFingerprint
    ) =>
        EndOfRunCleanFrameVisualObservation.Sampled(
            loadedCardCount,
            cardSetFingerprint,
            poseFingerprint
        );

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
