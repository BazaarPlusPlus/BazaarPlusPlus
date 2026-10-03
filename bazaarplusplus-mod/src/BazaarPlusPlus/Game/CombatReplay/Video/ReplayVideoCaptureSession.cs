#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureSession : IDisposable
{
    private readonly ReplayVideoCaptureRequest _request;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly double _frameInterval;
    private readonly object _finalizeLock = new();

    private RenderTexture? _captureRenderTexture;
    private IReplayVideoEncoder? _encoder;
    private MacMetalVideoEncoder? _metalEncoder;
    private WindowsMediaFoundationVideoEncoder? _windowsEncoder;
    private CommandBuffer? _metalCommandBuffer;
    private CommandBuffer? _windowsCommandBuffer;

    private WallClockCfrPacer? _pacer;
    private readonly ReplayVideoCopyTimingAccumulator _cfrCopyTiming = new();
    private readonly ReplayVideoCopyTimingAccumulator _renderFrameTiming = new();

    private long _lastEmittedSeq;
    private int _frameByteLength;

    private int _issuedSequence;
    private int _capturedFrames;
    private int _droppedFrames;
    private int _repeatedFrames;
    private int _nativeLeaseMisses;
    private int _nativeBackpressureDroppedFrames;
    private int _nativeEnqueueRejects;
    private int _nativePacerResyncDroppedFrames;
    private long _nativeOutputFrameIndex;
    private int? _previousMaxQueuedFrames;
    private bool _started;
    private bool _disposed;
    private bool _finalized;
    private ReplayVideoEncoderDrain? _finalizeDrain;
    private ReplayVideoRecordingReasonCode? _failureReasonCode;
    private Exception? _failureException;

    public ReplayVideoCaptureSession(ReplayVideoCaptureRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _startedAtUtc = DateTimeOffset.UtcNow;
        _frameInterval = 1.0 / Math.Max(1, request.Fps);
    }

    public ReplayVideoCaptureRequest Request => _request;

    public bool IsActive => _started && !_finalized && !_disposed && !_failureReasonCode.HasValue;

    public int CapturedFrames => _capturedFrames;

    public int DroppedFrames => _droppedFrames;

    public void Start()
    {
        if (_started)
            throw new InvalidOperationException("Session is already started.");

        EnsureOutputDirectory();
        var backend = ReplayVideoBackendPolicy.Current;
        if (backend == ReplayVideoBackend.WindowsNative)
        {
            _captureRenderTexture = new RenderTexture(
                _request.Width,
                _request.Height,
                depth: 0,
                format: RenderTextureFormat.ARGB32
            )
            {
                name = "BPP_CombatReplayVideoCapture",
                useMipMap = false,
                autoGenerateMips = false,
            };
            if (!_captureRenderTexture.Create())
            {
                UnityEngine.Object.Destroy(_captureRenderTexture);
                _captureRenderTexture = null;
                throw new InvalidOperationException(
                    $"Failed to create RenderTexture {_request.Width}x{_request.Height} for replay video capture."
                );
            }
        }

        _frameByteLength = _request.BufferPlan.FrameByteLength;
        _pacer = new WallClockCfrPacer(_request.Fps);

        if (backend == ReplayVideoBackend.MacNative)
        {
            _metalEncoder = new MacMetalVideoEncoder(
                _request.VideoId,
                _request.OutputFilePath,
                _request.Width,
                _request.Height,
                _request.Fps,
                _request.EncoderProfile.TargetBitrateKbps
            );
            _encoder = _metalEncoder;
        }
        else if (backend == ReplayVideoBackend.WindowsNative)
        {
            _windowsEncoder = new WindowsMediaFoundationVideoEncoder(
                _request.VideoId,
                _request.OutputFilePath,
                _request.Width,
                _request.Height,
                _request.Fps,
                _request.EncoderProfile.TargetBitrateKbps
            );
            _encoder = _windowsEncoder;
        }
        else
            throw new PlatformNotSupportedException(
                "Replay video recording supports macOS and Windows."
            );

        try
        {
            if (_metalEncoder != null)
            {
                _metalEncoder.Start();
                _metalCommandBuffer = new CommandBuffer
                {
                    name = "BPP Replay Metal VideoToolbox Submit",
                };
                _previousMaxQueuedFrames = QualitySettings.maxQueuedFrames;
                QualitySettings.maxQueuedFrames = Math.Max(3, _previousMaxQueuedFrames.Value);
            }
            else if (_windowsEncoder != null)
            {
                var sourceTexture = _captureRenderTexture?.GetNativeTexturePtr() ?? IntPtr.Zero;
                _windowsEncoder.Start(sourceTexture);
                _windowsCommandBuffer = new CommandBuffer
                {
                    name = "BPP Replay D3D11 Media Foundation Submit",
                };
                _previousMaxQueuedFrames = QualitySettings.maxQueuedFrames;
                QualitySettings.maxQueuedFrames = Math.Max(3, _previousMaxQueuedFrames.Value);
            }
        }
        catch
        {
            _metalEncoder?.SealCapture();
            _windowsEncoder?.SealCapture();
            ReleaseMetalCommandBuffer();
            ReleaseWindowsCommandBuffer();
            RestoreMetalCaptureScheduling();
            _encoder.Dispose();
            _encoder = null;
            _metalEncoder = null;
            _windowsEncoder = null;
            ReleaseRenderTexture();
            throw;
        }

        _started = true;

        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.video_capture.stats_observed"
            ),
            () =>
                [
                    ("recording_id", _request.VideoId, BppLogCorrelationPolicy.Short),
                    ("stage", ReplayVideoLogStage.CaptureStarted),
                    ("width", _request.Width),
                    ("height", _request.Height),
                    ("fps", _request.Fps),
                    ("captured_frames", 0),
                    ("repeated_frames", 0),
                    ("dropped_frames", 0),
                    ("duration_ms", 0),
                    ("size_bytes", 0),
                    ("output_path", _request.OutputFilePath),
                    ("codec", _request.EncoderProfile.Codec),
                    ("rate_control", _request.EncoderProfile.RateControlSummary),
                    ("frame_bytes", CalculateNv12FrameBytes(_request.Width, _request.Height)),
                    ("pool_capacity", _metalEncoder?.SlotCount ?? _windowsEncoder?.SlotCount ?? 0),
                    ("queue_capacity", _metalEncoder?.SlotCount ?? _windowsEncoder?.SlotCount ?? 0),
                    (
                        "pool_payload_bytes",
                        (long)(_metalEncoder?.SlotCount ?? _windowsEncoder?.SlotCount ?? 0)
                            * CalculateNv12FrameBytes(_request.Width, _request.Height)
                    ),
                    (
                        "pool_budget_exceeded",
                        (long)(_metalEncoder?.SlotCount ?? _windowsEncoder?.SlotCount ?? 0)
                            * CalculateNv12FrameBytes(_request.Width, _request.Height)
                            > ReplayVideoBufferPlan.DefaultPoolBudgetBytes
                    ),
                    ("cfr_copy_p95_us", 0),
                    ("staging_buffer_bytes", 0),
                    (
                        "render_texture_estimated_bytes",
                        _captureRenderTexture == null ? 0 : _frameByteLength
                    ),
                ]
        );
    }

    public void CaptureFrameIfDue()
    {
        if (
            !IsActive
            || _encoder == null
            || (_metalEncoder == null && _captureRenderTexture == null)
        )
            return;

        _renderFrameTiming.ObserveMicroseconds(
            (long)Math.Round(Math.Max(0, Time.unscaledDeltaTime) * 1_000_000d)
        );

        var encoder = _encoder;
        if (encoder.WriterFailed)
        {
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.EncoderWriterFailed;
            return;
        }

        var now = Time.unscaledTimeAsDouble;
        if (_metalEncoder != null)
        {
            CaptureMetalFrames(now, _metalEncoder);
            return;
        }
        if (_windowsEncoder != null)
            CaptureWindowsFrames(now, _windowsEncoder);
    }

    // The native path may encode only the texture visible on this render tick. Pacer resync slots
    // are omitted from the output timeline; native submission failures still advance PTS because
    // a real frame could not be encoded.
    private void CaptureMetalFrames(double now, MacMetalVideoEncoder encoder)
    {
        var pacer = _pacer;
        if (pacer == null)
            return;

        var sourceSequence = Interlocked.Increment(ref _issuedSequence);
        var tick = pacer.Tick(now, true, sourceSequence, ref _lastEmittedSeq);
        var plan = NativeFrameSubmissionPlan.Create(
            tick.EmitCount,
            tick.RepeatCount,
            tick.DroppedCount
        );
        _nativePacerResyncDroppedFrames += plan.PacerDroppedFrameCount;
        if (plan.EncodeFrameCount <= 0)
        {
            _droppedFrames += plan.PacerDroppedFrameCount;
            return;
        }

        var commandBuffer = _metalCommandBuffer;
        if (commandBuffer == null)
        {
            DropNativeSubmission(plan);
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
            return;
        }

        commandBuffer.Clear();
        var copyStarted = Stopwatch.GetTimestamp();
        var firstFrameIndex = _nativeOutputFrameIndex;
        if (!encoder.TryAcquireFrame(out var lease))
        {
            _nativeLeaseMisses++;
            _nativeBackpressureDroppedFrames += plan.EncodeFrameCount;
            DropNativeSubmission(plan);
            return;
        }

        if (
            !encoder.TryPrepareRenderEvent(
                lease,
                firstFrameIndex,
                plan.EncodeFrameCount,
                out var eventData
            )
        )
        {
            encoder.ReleaseFrame(lease);
            _nativeEnqueueRejects++;
            DropNativeSubmission(plan);
            return;
        }

        _nativeOutputFrameIndex += plan.TimelineFrameCount;

        var eventQueued = false;
        try
        {
            commandBuffer.IssuePluginEventAndData(
                encoder.RenderEventFunction,
                eventID: 1,
                eventData
            );
            eventQueued = true;
            Graphics.ExecuteCommandBuffer(commandBuffer);
            encoder.CommitRenderEvent(eventData);
            _capturedFrames += plan.CapturedFrameCount;
            _repeatedFrames += plan.RepeatedFrameCount;
            _droppedFrames += plan.PacerDroppedFrameCount;
        }
        catch (Exception ex)
        {
            if (eventQueued)
                encoder.CancelRenderEvent(eventData);
            else
                encoder.DiscardRenderEvent(eventData);
            _droppedFrames += plan.DroppedFrameCountOnSubmissionFailure;
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
            _failureException ??= ex;
        }
        finally
        {
            _cfrCopyTiming.ObserveSince(copyStarted);
        }
    }

    private void DropNativeSubmission(NativeFrameSubmissionPlan plan)
    {
        _droppedFrames += plan.DroppedFrameCountOnSubmissionFailure;
        _nativeOutputFrameIndex += plan.TimelineFrameCount;
    }

    private void CaptureWindowsFrames(double now, WindowsMediaFoundationVideoEncoder encoder)
    {
        var pacer = _pacer;
        var renderTexture = _captureRenderTexture;
        if (pacer == null || renderTexture == null)
            return;

        var sourceSequence = Interlocked.Increment(ref _issuedSequence);
        var tick = pacer.Tick(now, true, sourceSequence, ref _lastEmittedSeq);
        var plan = NativeFrameSubmissionPlan.Create(
            tick.EmitCount,
            tick.RepeatCount,
            tick.DroppedCount
        );
        _nativePacerResyncDroppedFrames += plan.PacerDroppedFrameCount;
        if (plan.EncodeFrameCount <= 0)
        {
            _droppedFrames += plan.PacerDroppedFrameCount;
            return;
        }

        var commandBuffer = _windowsCommandBuffer;
        if (commandBuffer == null)
        {
            DropNativeSubmission(plan);
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
            return;
        }

        commandBuffer.Clear();
        var copyStarted = Stopwatch.GetTimestamp();
        if (!encoder.TryAcquireFrame(out var lease))
        {
            _nativeLeaseMisses++;
            _nativeBackpressureDroppedFrames += plan.EncodeFrameCount;
            DropNativeSubmission(plan);
            return;
        }

        if (
            !encoder.TryPrepareRenderEvent(
                lease,
                _nativeOutputFrameIndex,
                plan.EncodeFrameCount,
                out var eventData
            )
        )
        {
            encoder.ReleaseFrame(lease);
            _nativeEnqueueRejects++;
            DropNativeSubmission(plan);
            return;
        }
        _nativeOutputFrameIndex += plan.TimelineFrameCount;

        var eventQueued = false;
        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(renderTexture);
            commandBuffer.IssuePluginEventAndData(
                encoder.RenderEventFunction,
                eventID: 1,
                eventData
            );
            eventQueued = true;
            Graphics.ExecuteCommandBuffer(commandBuffer);
            encoder.CommitRenderEvent(eventData);
            _capturedFrames += plan.CapturedFrameCount;
            _repeatedFrames += plan.RepeatedFrameCount;
            _droppedFrames += plan.PacerDroppedFrameCount;
        }
        catch (Exception ex)
        {
            if (eventQueued)
                encoder.CancelRenderEvent(eventData);
            else
                encoder.DiscardRenderEvent(eventData);
            _droppedFrames += plan.DroppedFrameCountOnSubmissionFailure;
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
            _failureException ??= ex;
        }
        finally
        {
            _cfrCopyTiming.ObserveSince(copyStarted);
        }
    }

    public ReplayVideoEncoderDrain Finalize(string endReason)
    {
        lock (_finalizeLock)
        {
            if (_finalizeDrain != null)
                return _finalizeDrain;

            _finalized = true;

            var encoder = _encoder;
            var metalEncoder = _metalEncoder;
            var windowsEncoder = _windowsEncoder;
            try
            {
                encoder?.SignalEndOfStream();
            }
            catch (Exception ex)
            {
                _failureReasonCode ??= ReplayVideoRecordingReasonCode.EncoderWriterFailed;
                _failureException ??= ex;
            }
            finally
            {
                _encoder = null;
            }

            metalEncoder?.SealCapture();
            windowsEncoder?.SealCapture();
            ReleaseMetalCommandBuffer();
            ReleaseWindowsCommandBuffer();
            RestoreMetalCaptureScheduling();
            if (metalEncoder != null)
                LogMetalCaptureStats();
            if (windowsEncoder != null)
                LogWindowsCaptureStats();

            ReleaseRenderTexture();

            _finalizeDrain = new ReplayVideoEncoderDrain(
                new ReplayVideoEncoderDrainInput(
                    encoder,
                    _request,
                    _startedAtUtc,
                    _capturedFrames,
                    _droppedFrames,
                    _repeatedFrames,
                    _failureReasonCode,
                    DegradationReasonCode: null,
                    _failureException,
                    _frameByteLength,
                    metalEncoder?.SlotCount ?? windowsEncoder?.SlotCount ?? 0,
                    _cfrCopyTiming.P95Microseconds
                )
            );
            _metalEncoder = null;
            _windowsEncoder = null;
            return _finalizeDrain;
        }
    }

    public void Dispose()
    {
        lock (_finalizeLock)
        {
            if (_disposed)
                return;
            _disposed = true;

            if (!_finalized)
                _failureReasonCode ??= ReplayVideoRecordingReasonCode.Aborted;

            if (!_finalized)
            {
                _metalEncoder?.SealCapture();
                _windowsEncoder?.SealCapture();
                ReleaseMetalCommandBuffer();
                ReleaseWindowsCommandBuffer();
                RestoreMetalCaptureScheduling();
                try
                {
                    _encoder?.Dispose();
                }
                catch
                {
                    // best effort
                }
                _encoder = null;
                _metalEncoder = null;
                _windowsEncoder = null;
            }

            _pacer = null;
            ReleaseMetalCommandBuffer();
            ReleaseWindowsCommandBuffer();
            RestoreMetalCaptureScheduling();
            ReleaseRenderTexture();
        }
    }

    private void RestoreMetalCaptureScheduling()
    {
        if (!_previousMaxQueuedFrames.HasValue)
            return;

        QualitySettings.maxQueuedFrames = _previousMaxQueuedFrames.Value;
        _previousMaxQueuedFrames = null;
    }

    private static long CalculateNv12FrameBytes(int width, int height) =>
        checked((long)width * height * 3 / 2);

    private void LogMetalCaptureStats()
    {
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.video_capture.native_pipeline_observed"
            ),
            () =>
                [
                    ("recording_id", _request.VideoId, BppLogCorrelationPolicy.Short),
                    ("stage", "metal_capture_sealed"),
                    ("backpressure_dropped_frames", _nativeBackpressureDroppedFrames),
                    ("dropped_frames", _droppedFrames),
                    ("lease_misses", _nativeLeaseMisses),
                    ("enqueue_rejects", _nativeEnqueueRejects),
                    ("pacer_resync_dropped_frames", _nativePacerResyncDroppedFrames),
                    ("render_frame_p50_us", _renderFrameTiming.P50Microseconds),
                    ("render_frame_p95_us", _renderFrameTiming.P95Microseconds),
                    ("render_frame_p99_us", _renderFrameTiming.P99Microseconds),
                    ("texture_copy_p50_us", _cfrCopyTiming.P50Microseconds),
                    ("texture_copy_p95_us", _cfrCopyTiming.P95Microseconds),
                    ("texture_copy_p99_us", _cfrCopyTiming.P99Microseconds),
                ]
        );
    }

    private void LogWindowsCaptureStats()
    {
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.video_capture.native_pipeline_observed"
            ),
            () =>
                [
                    ("recording_id", _request.VideoId, BppLogCorrelationPolicy.Short),
                    ("stage", "d3d11_mf_capture_sealed"),
                    ("backpressure_dropped_frames", _nativeBackpressureDroppedFrames),
                    ("dropped_frames", _droppedFrames),
                    ("lease_misses", _nativeLeaseMisses),
                    ("enqueue_rejects", _nativeEnqueueRejects),
                    ("pacer_resync_dropped_frames", _nativePacerResyncDroppedFrames),
                    ("render_frame_p50_us", _renderFrameTiming.P50Microseconds),
                    ("render_frame_p95_us", _renderFrameTiming.P95Microseconds),
                    ("render_frame_p99_us", _renderFrameTiming.P99Microseconds),
                    ("texture_copy_p50_us", _cfrCopyTiming.P50Microseconds),
                    ("texture_copy_p95_us", _cfrCopyTiming.P95Microseconds),
                    ("texture_copy_p99_us", _cfrCopyTiming.P99Microseconds),
                ]
        );
    }

    private void ReleaseRenderTexture()
    {
        var rt = _captureRenderTexture;
        if (rt == null)
            return;

        _captureRenderTexture = null;
        try
        {
            if (rt.IsCreated())
                rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.video_recording.cleanup_failed"
                ),
                ex,
                () =>
                    [
                        ("recording_id", _request.VideoId, BppLogCorrelationPolicy.Short),
                        ("stage", ReplayVideoLogStage.RenderTextureRelease),
                        ("path", null),
                    ]
            );
        }
    }

    private void ReleaseMetalCommandBuffer()
    {
        var commandBuffer = _metalCommandBuffer;
        if (commandBuffer == null)
            return;

        _metalCommandBuffer = null;
        commandBuffer.Release();
    }

    private void ReleaseWindowsCommandBuffer()
    {
        var commandBuffer = _windowsCommandBuffer;
        if (commandBuffer == null)
            return;

        _windowsCommandBuffer = null;
        commandBuffer.Release();
    }

    private void EnsureOutputDirectory()
    {
        var directory = Path.GetDirectoryName(_request.OutputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }
}
