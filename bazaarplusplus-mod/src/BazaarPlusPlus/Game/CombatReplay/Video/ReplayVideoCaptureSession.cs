#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureSession : IDisposable
{
    private readonly ReplayVideoCaptureRequest _request;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly double _frameInterval;
    private readonly object _finalizeLock = new();

    // Reused across every captured frame for the in-place vertical flip so we
    // do not allocate a fresh per-row scratch buffer on each readback. Only
    // touched from the AsyncGPUReadback completion callback, which Unity invokes
    // on the main thread (the same thread that drives capture/finalize), so no
    // synchronization is required.
    private byte[]? _flipRowBuffer;
    private RenderTexture? _captureRenderTexture;
    private FfmpegRawVideoEncoder? _encoder;

    // Frame pool (eliminates the per-frame width*height*4 allocation) and the
    // wall-clock CFR pacer (decouples capture rhythm from the constant fps feed
    // rate handed to ffmpeg). The pool's Return runs on the encoder writer
    // thread; everything else here runs on the Unity main thread.
    private ReplayVideoFramePool? _pool;
    private WallClockCfrPacer? _pacer;
    private readonly ReplayVideoReadbackLimiter _readbackLimiter = new(limit: 2);
    private readonly ReplayVideoCopyTimingAccumulator _readbackCopyTiming = new();
    private readonly ReplayVideoCopyTimingAccumulator _cfrCopyTiming = new();

    // The single non-pooled staging buffer that OnReadbackComplete overwrites in
    // place. It is never enqueued and never returned to the pool; every emit
    // copies it into a fresh pooled buffer, so CFR repeats are safe.
    private byte[]? _latestFrameBuffer;
    private long _latestSeq;
    private long _lastEmittedSeq;
    private bool _hasLatest;
    private int _frameByteLength;

    private double _nextCaptureTime;
    private int _issuedSequence;
    private int _readbackBackpressureSkips;
    private int _capturedFrames;
    private int _droppedFrames;
    private int _repeatedFrames;
    private bool _started;
    private bool _disposed;
    private bool _finalized;
    private ReplayVideoEncoderDrain? _finalizeDrain;
    private ReplayVideoRecordingReasonCode? _failureReasonCode;
    private ReplayVideoRecordingReasonCode? _degradationReasonCode;
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

        _frameByteLength = _request.BufferPlan.FrameByteLength;
        _latestFrameBuffer = new byte[_frameByteLength];
        _pool = new ReplayVideoFramePool(_frameByteLength, _request.BufferPlan.PoolCapacity);
        _pacer = new WallClockCfrPacer(_request.Fps);

        _encoder = new FfmpegRawVideoEncoder(
            _request.VideoId,
            _request.FfmpegExecutable,
            _request.OutputFilePath,
            _request.Width,
            _request.Height,
            _request.Fps,
            _request.EncoderProfile,
            _request.BufferPlan.QueueCapacity,
            onFrameConsumed: buf => _pool?.Return(buf)
        );

        try
        {
            _encoder.Start();
        }
        catch
        {
            if (_request.EncoderProfile.HardwareAccelerated)
            {
                FfmpegVideoEncoderSelector.Invalidate(
                    _request.FfmpegExecutable,
                    _request.Width,
                    _request.Height,
                    _request.Fps,
                    _request.EncoderProfile.Codec
                );
            }
            _encoder.Dispose();
            _encoder = null;
            ReleaseRenderTexture();
            throw;
        }

        _nextCaptureTime = Time.unscaledTimeAsDouble;
        _started = true;

        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.VideoCaptureStatsObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.StatsRecordingId.Bind(_request.VideoId),
                    CombatReplayVideoLogEvents.StatsStage.Bind(ReplayVideoLogStage.CaptureStarted),
                    CombatReplayVideoLogEvents.StatsWidth.Bind(_request.Width),
                    CombatReplayVideoLogEvents.StatsHeight.Bind(_request.Height),
                    CombatReplayVideoLogEvents.StatsFps.Bind(_request.Fps),
                    CombatReplayVideoLogEvents.StatsCapturedFrames.Bind(0),
                    CombatReplayVideoLogEvents.StatsRepeatedFrames.Bind(0),
                    CombatReplayVideoLogEvents.StatsDroppedFrames.Bind(0),
                    CombatReplayVideoLogEvents.StatsDurationMs.Bind(0),
                    CombatReplayVideoLogEvents.StatsSizeBytes.Bind(0),
                    CombatReplayVideoLogEvents.StatsOutputPath.Bind(_request.OutputFilePath),
                    CombatReplayVideoLogEvents.StatsCodec.Bind(_request.EncoderProfile.Codec),
                    CombatReplayVideoLogEvents.StatsRateControl.Bind(
                        _request.EncoderProfile.RateControlSummary
                    ),
                    CombatReplayVideoLogEvents.StatsFrameBytes.Bind(_frameByteLength),
                    CombatReplayVideoLogEvents.StatsPoolCapacity.Bind(
                        _request.BufferPlan.PoolCapacity
                    ),
                    CombatReplayVideoLogEvents.StatsQueueCapacity.Bind(
                        _request.BufferPlan.QueueCapacity
                    ),
                    CombatReplayVideoLogEvents.StatsPoolPayloadBytes.Bind(
                        _request.BufferPlan.PoolPayloadBytes
                    ),
                    CombatReplayVideoLogEvents.StatsPoolBudgetExceeded.Bind(
                        _request.BufferPlan.BudgetExceeded
                    ),
                    CombatReplayVideoLogEvents.StatsReadbackBackpressureSkips.Bind(0),
                    CombatReplayVideoLogEvents.StatsMaxOutstandingReadbacks.Bind(0),
                    CombatReplayVideoLogEvents.StatsReadbackCopyP95Us.Bind(0),
                    CombatReplayVideoLogEvents.StatsCfrCopyP95Us.Bind(0),
                    CombatReplayVideoLogEvents.StatsStagingBufferBytes.Bind(_frameByteLength),
                    CombatReplayVideoLogEvents.StatsMaxReadbackPayloadBytes.Bind(0),
                    CombatReplayVideoLogEvents.StatsRenderTextureEstimatedBytes.Bind(
                        _frameByteLength
                    ),
                ]
        );
    }

    public void CaptureFrameIfDue()
    {
        if (!IsActive || _encoder == null || _captureRenderTexture == null)
            return;

        var encoder = _encoder;
        if (encoder.WriterFailed)
        {
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.EncoderWriterFailed;
            return;
        }

        var now = Time.unscaledTimeAsDouble;
        if (now >= _nextCaptureTime)
        {
            TryRequestReadback();
            _nextCaptureTime += _frameInterval;
        }

        if (now - _nextCaptureTime > _frameInterval * 5)
        {
            _nextCaptureTime = now + _frameInterval;
        }

        EmitDueFrames(now);
    }

    // Wall-clock CFR emit beat: runs on the same main-thread coroutine tick as
    // capture. The pacer decides how many constant-fps slots elapsed; for each
    // slot we copy the (non-pooled) staging frame into a fresh pooled buffer and
    // enqueue it exactly once. The encoder's frame-consumed callback returns the
    // buffer to the pool on the writer thread, so each repeat is a distinct
    // buffer and there is no use-after-return.
    private void EmitDueFrames(double now)
    {
        var pacer = _pacer;
        var encoder = _encoder;
        var pool = _pool;
        if (pacer == null || encoder == null || pool == null || !_hasLatest)
            return;

        var tick = pacer.Tick(now, _hasLatest, _latestSeq, ref _lastEmittedSeq);

        // The latest source sequence is constant across this tick (no new
        // readback lands mid-loop on the main thread), so the new-source slots
        // come first and the rest are repeats. Drive captured/repeated purely
        // from this split; rent/enqueue failures count as dropped.
        var newSlots = tick.EmitCount - tick.RepeatCount;
        for (var i = 0; i < tick.EmitCount; i++)
        {
            if (encoder.WriterFailed)
            {
                _failureReasonCode ??= ReplayVideoRecordingReasonCode.EncoderWriterFailed;
                break;
            }

            var isNew = i < newSlots;

            var buffer = pool.Rent();
            if (buffer == null)
            {
                _droppedFrames++;
                continue;
            }

            var copyStarted = Stopwatch.GetTimestamp();
            Buffer.BlockCopy(_latestFrameBuffer!, 0, buffer, 0, _frameByteLength);
            _cfrCopyTiming.ObserveSince(copyStarted);

            if (encoder.TryEnqueueFrame(buffer))
            {
                if (isNew)
                    _capturedFrames++;
                else
                    _repeatedFrames++;
            }
            else
            {
                pool.Return(buffer);
                _droppedFrames++;
            }
        }

        _droppedFrames += tick.DroppedCount;
    }

    public ReplayVideoEncoderDrain Finalize(string endReason)
    {
        lock (_finalizeLock)
        {
            if (_finalizeDrain != null)
                return _finalizeDrain;

            _finalized = true;

            try
            {
                AsyncGPUReadback.WaitAllRequests();
            }
            catch (Exception ex)
            {
                _degradationReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
                _failureException ??= ex;
            }

            try
            {
                EmitFinalFrame();
            }
            catch (Exception ex)
            {
                _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
                _failureException ??= ex;
            }

            var encoder = _encoder;
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
                    _degradationReasonCode,
                    _failureException,
                    _frameByteLength,
                    _readbackBackpressureSkips,
                    _readbackLimiter.MaxObserved,
                    _readbackCopyTiming.P95Microseconds,
                    _cfrCopyTiming.P95Microseconds
                )
            );
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

            // A handed-off encoder may still return a frame on its writer thread. The callback reads
            // _pool through a null-safe closure, so clearing the session reference is race-safe.
            _pool = null;
            _pacer = null;
            _latestFrameBuffer = null;
            ReleaseRenderTexture();
        }
    }

    private bool TryRequestReadback()
    {
        var rt = _captureRenderTexture;
        if (rt == null)
            return false;
        if (!_readbackLimiter.TryReserve())
        {
            _readbackBackpressureSkips++;
            return false;
        }

        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            var sequenceNumber = Interlocked.Increment(ref _issuedSequence);
            AsyncGPUReadback.Request(rt, 0, request => OnReadbackComplete(request, sequenceNumber));
            return true;
        }
        catch (Exception ex)
        {
            _readbackLimiter.Release();
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.CaptureFailed;
            _failureException ??= ex;
            return false;
        }
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request, int sequenceNumber)
    {
        _readbackLimiter.Release();

        if (_disposed)
            return;

        if (request.hasError)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoCaptureFrameDegraded,
                () =>
                    [
                        CombatReplayVideoLogEvents.FrameRecordingId.Bind(_request.VideoId),
                        CombatReplayVideoLogEvents.FrameStage.Bind(ReplayVideoLogStage.Readback),
                        CombatReplayVideoLogEvents.FrameReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.ReadbackFailed
                        ),
                        CombatReplayVideoLogEvents.FrameSequence.Bind(sequenceNumber),
                    ]
            );
            _droppedFrames++;
            return;
        }

        // Out-of-order completion guard: a newer readback may have already landed
        // and become the latest staging frame. Never let a stale (lower or equal
        // sequence) readback overwrite it.
        if (sequenceNumber <= _latestSeq)
        {
            _droppedFrames++;
            return;
        }

        // If the currently adopted latest was never emitted, its content is about
        // to be lost as we overwrite the single staging buffer: count it dropped.
        if (_hasLatest && _latestSeq != _lastEmittedSeq)
            _droppedFrames++;

        try
        {
            var data = request.GetData<byte>();
            // Copy into the single reusable staging buffer (sized exactly
            // width*height*4); we never enqueue this buffer, so reusing it is
            // safe and eliminates the per-frame allocation. Each emit copies it
            // into a fresh pooled buffer.
            var copyStarted = Stopwatch.GetTimestamp();
            data.CopyTo(_latestFrameBuffer!);
            _readbackCopyTiming.ObserveSince(copyStarted);
            // AsyncGPUReadback returns rows in the graphics API's native vertical order, and
            // ffmpeg's rawvideo input treats row 0 as the top of the frame. On top-origin APIs
            // (D3D/Metal/Vulkan; SystemInfo.graphicsUVStartsAtTop == true) row 0 is already the
            // top, so flipping would record the video upside down. Only bottom-origin APIs
            // (OpenGL) need the vertical flip.
            if (!SystemInfo.graphicsUVStartsAtTop)
                FlipVerticalRgba32(_latestFrameBuffer!, _request.Width, _request.Height);
            _latestSeq = sequenceNumber;
            _hasLatest = true;
        }
        catch (Exception ex)
        {
            _droppedFrames++;
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoCaptureFrameDegraded,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.FrameRecordingId.Bind(_request.VideoId),
                        CombatReplayVideoLogEvents.FrameStage.Bind(ReplayVideoLogStage.Readback),
                        CombatReplayVideoLogEvents.FrameReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.ReadbackFailed
                        ),
                        CombatReplayVideoLogEvents.FrameSequence.Bind(sequenceNumber),
                    ]
            );
        }
    }

    // Final emit at finalize: WaitAllRequests above has drained every readback,
    // so _latestFrameBuffer holds the last captured frame. If that distinct
    // frame was never emitted by the wall-clock beat, push it once so the tail
    // frame lands in the stream. Trailing wall-clock pad is deferred (v1).
    private void EmitFinalFrame()
    {
        var encoder = _encoder;
        var pool = _pool;
        if (encoder == null || pool == null || !_hasLatest || _latestSeq == _lastEmittedSeq)
            return;

        if (encoder.WriterFailed)
        {
            _failureReasonCode ??= ReplayVideoRecordingReasonCode.EncoderWriterFailed;
            return;
        }

        var buffer = pool.Rent();
        if (buffer == null)
        {
            _droppedFrames++;
            return;
        }

        var copyStarted = Stopwatch.GetTimestamp();
        Buffer.BlockCopy(_latestFrameBuffer!, 0, buffer, 0, _frameByteLength);
        _cfrCopyTiming.ObserveSince(copyStarted);

        if (encoder.TryEnqueueFrame(buffer))
        {
            _capturedFrames++;
            _lastEmittedSeq = _latestSeq;
        }
        else
        {
            pool.Return(buffer);
            _droppedFrames++;
        }
    }

    // In-place vertical flip identical to Rgba32FrameTransforms.FlipVerticalRgba32,
    // but reusing the per-session _flipRowBuffer field instead of allocating a fresh
    // row-sized scratch buffer on every frame. Invoked only from OnReadbackComplete on
    // the Unity main thread, so the shared field needs no synchronization.
    private void FlipVerticalRgba32(byte[] buffer, int width, int height)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        if (width <= 0 || height <= 0)
            return;

        var stride = width * 4;
        var expectedLength = stride * height;
        if (buffer.Length < expectedLength)
            return;

        var rowBuffer = _flipRowBuffer;
        if (rowBuffer == null || rowBuffer.Length < stride)
        {
            rowBuffer = new byte[stride];
            _flipRowBuffer = rowBuffer;
        }

        for (var row = 0; row < height / 2; row++)
        {
            var topOffset = row * stride;
            var bottomOffset = (height - 1 - row) * stride;

            Buffer.BlockCopy(buffer, topOffset, rowBuffer, 0, stride);
            Buffer.BlockCopy(buffer, bottomOffset, buffer, topOffset, stride);
            Buffer.BlockCopy(rowBuffer, 0, buffer, bottomOffset, stride);
        }
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
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(_request.VideoId),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.RenderTextureRelease
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(null),
                    ]
            );
        }
    }

    private void EnsureOutputDirectory()
    {
        var directory = Path.GetDirectoryName(_request.OutputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }
}
