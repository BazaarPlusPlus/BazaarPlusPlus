#nullable enable
using BazaarPlusPlus.Infrastructure;

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
    int ReadbackBackpressureSkips,
    int MaxOutstandingReadbacks,
    long ReadbackCopyP95Us,
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
            BattleId = request.BattleId,
            Source = request.Source,
            OutputFilePath = request.OutputFilePath,
            Width = request.Width,
            Height = request.Height,
            Fps = request.Fps,
            Codec = request.EncoderProfile.Codec,
            Crf = request.EncoderProfile.Crf,
            Preset = request.EncoderProfile.Preset,
            StartedAtUtc = _input.StartedAtUtc,
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
            CombatReplayVideoLogEvents.VideoCaptureStatsObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.StatsRecordingId.Bind(request.VideoId),
                    CombatReplayVideoLogEvents.StatsStage.Bind(
                        ReplayVideoLogStage.CaptureFinalized
                    ),
                    CombatReplayVideoLogEvents.StatsWidth.Bind(request.Width),
                    CombatReplayVideoLogEvents.StatsHeight.Bind(request.Height),
                    CombatReplayVideoLogEvents.StatsFps.Bind(request.Fps),
                    CombatReplayVideoLogEvents.StatsCapturedFrames.Bind(result.CapturedFrames),
                    CombatReplayVideoLogEvents.StatsRepeatedFrames.Bind(_input.RepeatedFrames),
                    CombatReplayVideoLogEvents.StatsDroppedFrames.Bind(result.DroppedFrames),
                    CombatReplayVideoLogEvents.StatsDurationMs.Bind(result.DurationMs),
                    CombatReplayVideoLogEvents.StatsSizeBytes.Bind(result.FileSizeBytes),
                    CombatReplayVideoLogEvents.StatsOutputPath.Bind(result.OutputFilePath),
                    CombatReplayVideoLogEvents.StatsCodec.Bind(request.EncoderProfile.Codec),
                    CombatReplayVideoLogEvents.StatsRateControl.Bind(
                        request.EncoderProfile.RateControlSummary
                    ),
                    CombatReplayVideoLogEvents.StatsFrameBytes.Bind(frameByteLength),
                    CombatReplayVideoLogEvents.StatsPoolCapacity.Bind(poolCapacity),
                    CombatReplayVideoLogEvents.StatsQueueCapacity.Bind(queueCapacity),
                    CombatReplayVideoLogEvents.StatsPoolPayloadBytes.Bind(poolPayloadBytes),
                    CombatReplayVideoLogEvents.StatsPoolBudgetExceeded.Bind(poolBudgetExceeded),
                    CombatReplayVideoLogEvents.StatsReadbackBackpressureSkips.Bind(
                        _input.ReadbackBackpressureSkips
                    ),
                    CombatReplayVideoLogEvents.StatsMaxOutstandingReadbacks.Bind(
                        _input.MaxOutstandingReadbacks
                    ),
                    CombatReplayVideoLogEvents.StatsReadbackCopyP95Us.Bind(
                        _input.ReadbackCopyP95Us
                    ),
                    CombatReplayVideoLogEvents.StatsCfrCopyP95Us.Bind(_input.CfrCopyP95Us),
                    CombatReplayVideoLogEvents.StatsStagingBufferBytes.Bind(
                        usesNativeSlots ? 0 : frameByteLength
                    ),
                    CombatReplayVideoLogEvents.StatsMaxReadbackPayloadBytes.Bind(
                        (long)_input.MaxOutstandingReadbacks * frameByteLength
                    ),
                    CombatReplayVideoLogEvents.StatsRenderTextureEstimatedBytes.Bind(
                        usesNativeSlots ? 0 : frameByteLength
                    ),
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
