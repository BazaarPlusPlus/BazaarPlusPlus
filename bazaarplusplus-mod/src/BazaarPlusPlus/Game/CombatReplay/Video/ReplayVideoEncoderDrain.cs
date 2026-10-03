#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed record ReplayVideoEncoderDrainInput(
    IReplayVideoEncoder? Encoder,
    ReplayVideoCaptureRequest Request,
    DateTimeOffset StartedAtUtc,
    int CapturedFrames,
    int DroppedFrames,
    int RepeatedFrames,
    ReplayVideoRecordingReasonCode? FailureReasonCode,
    ReplayVideoRecordingReasonCode? DegradationReasonCode,
    Exception? FailureException,
    int FrameByteLength,
    int NativeSlotCount,
    long CfrCopyP95Us
);

/// <summary>
/// Owns a sealed encoder after the Unity capture session hands it off. Completion is safe to call
/// concurrently: one caller drains and disposes the encoder, and every caller observes the same
/// immutable capture result. This type deliberately has no Unity dependencies.
/// </summary>
internal sealed class ReplayVideoEncoderDrain
{
    private readonly ReplayVideoEncoderDrainInput _input;
    private readonly Lazy<ReplayVideoCaptureResult> _completion;

    internal ReplayVideoEncoderDrain(ReplayVideoEncoderDrainInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _completion = new Lazy<ReplayVideoCaptureResult>(
            CompleteOnce,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    internal ReplayVideoCaptureResult Complete() => _completion.Value;

    private ReplayVideoCaptureResult CompleteOnce()
    {
        var request = _input.Request;
        var failureReasonCode = _input.FailureReasonCode;
        var failureException = _input.FailureException;
        var exitCode = (int?)null;
        var stderrTail = (string?)null;
        var encoder = _input.Encoder;

        if (encoder != null)
        {
            try
            {
                var outcome = encoder.WaitForCompletion(TimeSpan.FromSeconds(20));
                exitCode = outcome.ExitCode;
                stderrTail = outcome.StderrTail;
                if (!outcome.Succeeded)
                {
                    failureReasonCode ??= MapReason(outcome.ReasonCode);
                    failureException ??= outcome.Exception;
                }
            }
            catch (Exception ex)
            {
                failureReasonCode ??= ReplayVideoRecordingReasonCode.EncoderWriterFailed;
                failureException ??= ex;
            }
            finally
            {
                encoder.Dispose();
            }
        }

        var endedAt = DateTimeOffset.UtcNow;
        var durationMs = (long)Math.Max(0, (endedAt - _input.StartedAtUtc).TotalMilliseconds);
        var fileSize = ReplayVideoFileHelpers.TryGetFileSize(request.OutputFilePath);
        var status =
            failureReasonCode.HasValue || fileSize <= 0
                ? ReplayVideoCaptureStatus.Failed
                : (
                    _input.CapturedFrames > 0
                        ? ReplayVideoCaptureStatus.Completed
                        : ReplayVideoCaptureStatus.Failed
                );
        var reasonCode =
            failureReasonCode
            ?? (
                status == ReplayVideoCaptureStatus.Failed
                    ? ReplayVideoRecordingReasonCode.CaptureFailed
                    : _input.DegradationReasonCode ?? ReplayVideoRecordingReasonCode.Completed
            );
        var result = new ReplayVideoCaptureResult
        {
            VideoId = request.VideoId,
            OutputFilePath = request.OutputFilePath,
            EndedAtUtc = endedAt,
            DurationMs = durationMs,
            CapturedFrames = _input.CapturedFrames,
            DroppedFrames = _input.DroppedFrames,
            FileSizeBytes = fileSize,
            Status = status,
            Error = status == ReplayVideoCaptureStatus.Failed ? reasonCode.ToString() : null,
            ReasonCode = reasonCode,
            ExitCode = exitCode,
            StderrTail = stderrTail,
            Exception = failureException,
            Degraded = _input.DegradationReasonCode.HasValue,
        };

        LogCaptureFinalized(result);
        return result;
    }

    private void LogCaptureFinalized(ReplayVideoCaptureResult result)
    {
        var request = _input.Request;
        var usesNativeSlots = _input.NativeSlotCount > 0;
        var frameByteLength = usesNativeSlots
            ? checked((int)((long)request.Width * request.Height * 3 / 2))
            : _input.FrameByteLength;
        var poolCapacity = usesNativeSlots
            ? _input.NativeSlotCount
            : request.BufferPlan.PoolCapacity;
        var queueCapacity = usesNativeSlots
            ? _input.NativeSlotCount
            : request.BufferPlan.QueueCapacity;
        var poolPayloadBytes = usesNativeSlots
            ? checked((long)frameByteLength * poolCapacity)
            : request.BufferPlan.PoolPayloadBytes;
        var poolBudgetExceeded = usesNativeSlots
            ? poolPayloadBytes > ReplayVideoBufferPlan.DefaultPoolBudgetBytes
            : request.BufferPlan.BudgetExceeded;
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.video_capture.stats_observed"
            ),
            () =>
                [
                    ("recording_id", request.VideoId, BppLogCorrelationPolicy.Short),
                    ("stage", ReplayVideoLogStage.CaptureFinalized),
                    ("width", request.Width),
                    ("height", request.Height),
                    ("fps", request.Fps),
                    ("captured_frames", result.CapturedFrames),
                    ("repeated_frames", _input.RepeatedFrames),
                    ("dropped_frames", result.DroppedFrames),
                    ("duration_ms", result.DurationMs),
                    ("size_bytes", result.FileSizeBytes),
                    ("output_path", result.OutputFilePath),
                    ("codec", request.EncoderProfile.Codec),
                    ("rate_control", request.EncoderProfile.RateControlSummary),
                    ("frame_bytes", frameByteLength),
                    ("pool_capacity", poolCapacity),
                    ("queue_capacity", queueCapacity),
                    ("pool_payload_bytes", poolPayloadBytes),
                    ("pool_budget_exceeded", poolBudgetExceeded),
                    ("cfr_copy_p95_us", _input.CfrCopyP95Us),
                    ("staging_buffer_bytes", usesNativeSlots ? 0 : frameByteLength),
                    ("render_texture_estimated_bytes", usesNativeSlots ? 0 : frameByteLength),
                ]
        );
    }

    internal static ReplayVideoRecordingReasonCode MapReason(
        ReplayVideoEncoderFailureReasonCode reasonCode
    ) =>
        reasonCode switch
        {
            ReplayVideoEncoderFailureReasonCode.NonZeroExit =>
                ReplayVideoRecordingReasonCode.EncoderNonZeroExit,
            ReplayVideoEncoderFailureReasonCode.WriterTimeout
            or ReplayVideoEncoderFailureReasonCode.ProcessTimeout =>
                ReplayVideoRecordingReasonCode.EncoderTimeout,
            _ => ReplayVideoRecordingReasonCode.EncoderWriterFailed,
        };
}
