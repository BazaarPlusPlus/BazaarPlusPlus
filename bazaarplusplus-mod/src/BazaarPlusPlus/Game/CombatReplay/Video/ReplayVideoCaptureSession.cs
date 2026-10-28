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
    private readonly object _finalizeLock = new();

    private readonly NativeFrameSubmission _frames;
    private INativeFrameTarget? _frameTarget;
    private RenderTexture? _captureRenderTexture;
    private IReplayVideoEncoder? _encoder;
    private MacMetalVideoEncoder? _metalEncoder;
    private WindowsMediaFoundationVideoEncoder? _windowsEncoder;
    private CommandBuffer? _metalCommandBuffer;
    private CommandBuffer? _windowsCommandBuffer;

    private readonly ReplayVideoCopyTimingAccumulator _renderFrameTiming = new();

    private int _frameByteLength;

    private int? _previousMaxQueuedFrames;
    private readonly long _constructedTimestamp = Stopwatch.GetTimestamp();
    private long _startedTimestamp;
    private long _finalizedTimestamp;
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
        _frames = new NativeFrameSubmission(request.Fps);
    }

    public ReplayVideoCaptureRequest Request => _request;

    public bool IsActive => _started && !_finalized && !_disposed && !_failureReasonCode.HasValue;

    public int CapturedFrames => _frames.CapturedFrames;

    public int DroppedFrames => _frames.DroppedFrames;

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
                _frameTarget = new MetalFrameTarget(_metalEncoder, _metalCommandBuffer);
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
                _frameTarget = new WindowsFrameTarget(
                    _windowsEncoder,
                    _windowsCommandBuffer,
                    _captureRenderTexture!
                );
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
        _startedTimestamp = Stopwatch.GetTimestamp();

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

        _frames.Capture(Time.unscaledTimeAsDouble, _frameTarget);
        if (_frames.Failed)
        {
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
            _failureException ??= _frames.FailureException;
        }
    }

    public ReplayVideoEncoderDrain Finalize(string endReason)
    {
        lock (_finalizeLock)
        {
            if (_finalizeDrain != null)
                return _finalizeDrain;

            _finalized = true;
            _finalizedTimestamp = Stopwatch.GetTimestamp();

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
                    _frames.CapturedFrames,
                    _frames.DroppedFrames,
                    _frames.RepeatedFrames,
                    _failureReasonCode,
                    DegradationReasonCode: null,
                    _failureException,
                    _frameByteLength,
                    metalEncoder?.SlotCount ?? windowsEncoder?.SlotCount ?? 0,
                    _frames.CopyTiming.P95Microseconds,
                    _frames.OutputFrameIndex,
                    new ReplayVideoSessionTimeline(
                        _constructedTimestamp,
                        _startedTimestamp,
                        _frames.FirstFrameTimestamp,
                        _finalizedTimestamp
                    )
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
                    ("backpressure_dropped_frames", _frames.BackpressureDroppedFrames),
                    ("dropped_frames", _frames.DroppedFrames),
                    ("lease_misses", _frames.LeaseMisses),
                    ("enqueue_rejects", _frames.EnqueueRejects),
                    ("pacer_resync_dropped_frames", _frames.PacerResyncDroppedFrames),
                    ("render_frame_p50_us", _renderFrameTiming.P50Microseconds),
                    ("render_frame_p95_us", _renderFrameTiming.P95Microseconds),
                    ("render_frame_p99_us", _renderFrameTiming.P99Microseconds),
                    ("texture_copy_p50_us", _frames.CopyTiming.P50Microseconds),
                    ("texture_copy_p95_us", _frames.CopyTiming.P95Microseconds),
                    ("texture_copy_p99_us", _frames.CopyTiming.P99Microseconds),
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
                    ("backpressure_dropped_frames", _frames.BackpressureDroppedFrames),
                    ("dropped_frames", _frames.DroppedFrames),
                    ("lease_misses", _frames.LeaseMisses),
                    ("enqueue_rejects", _frames.EnqueueRejects),
                    ("pacer_resync_dropped_frames", _frames.PacerResyncDroppedFrames),
                    ("render_frame_p50_us", _renderFrameTiming.P50Microseconds),
                    ("render_frame_p95_us", _renderFrameTiming.P95Microseconds),
                    ("render_frame_p99_us", _renderFrameTiming.P99Microseconds),
                    ("texture_copy_p50_us", _frames.CopyTiming.P50Microseconds),
                    ("texture_copy_p95_us", _frames.CopyTiming.P95Microseconds),
                    ("texture_copy_p99_us", _frames.CopyTiming.P99Microseconds),
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
        _frameTarget = null;
        commandBuffer.Release();
    }

    private void ReleaseWindowsCommandBuffer()
    {
        var commandBuffer = _windowsCommandBuffer;
        if (commandBuffer == null)
            return;

        _windowsCommandBuffer = null;
        _frameTarget = null;
        commandBuffer.Release();
    }

    private void EnsureOutputDirectory()
    {
        var directory = Path.GetDirectoryName(_request.OutputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }
}
