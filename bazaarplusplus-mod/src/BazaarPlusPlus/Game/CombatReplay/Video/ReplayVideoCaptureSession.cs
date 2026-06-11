#nullable enable
using System;
using System.IO;
using System.Threading;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureSession : IDisposable
{
    private readonly ReplayVideoCaptureRequest _request;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly double _frameInterval;
    private readonly object _disposeLock = new();

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
    private int _outstandingReadbackCount;
    private int _capturedFrames;
    private int _droppedFrames;
    private int _repeatedFrames;
    private bool _started;
    private bool _disposed;
    private bool _finalized;
    private string? _failureReason;

    public ReplayVideoCaptureSession(ReplayVideoCaptureRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _startedAtUtc = DateTimeOffset.UtcNow;
        _frameInterval = 1.0 / Math.Max(1, request.Fps);
    }

    public ReplayVideoCaptureRequest Request => _request;

    public bool IsActive => _started && !_finalized && !_disposed && _failureReason == null;

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

        _frameByteLength = _request.Width * _request.Height * 4;
        _latestFrameBuffer = new byte[_frameByteLength];
        _pool = new ReplayVideoFramePool(_frameByteLength, _request.MaxQueuedFrames);
        _pacer = new WallClockCfrPacer(_request.Fps);

        _encoder = new FfmpegRawVideoEncoder(
            _request.FfmpegExecutable,
            _request.OutputFilePath,
            _request.Width,
            _request.Height,
            _request.Fps,
            _request.Crf,
            _request.Preset,
            _request.MaxQueuedFrames,
            onFrameConsumed: buf => _pool?.Return(buf)
        );

        try
        {
            _encoder.Start();
        }
        catch
        {
            _encoder.Dispose();
            _encoder = null;
            ReleaseRenderTexture();
            throw;
        }

        _nextCaptureTime = Time.unscaledTimeAsDouble;
        _started = true;

        BppLog.Info(
            "CombatReplayVideo",
            $"Frame orientation: graphicsUVStartsAtTop={SystemInfo.graphicsUVStartsAtTop} verticalFlip={!SystemInfo.graphicsUVStartsAtTop}"
        );
    }

    public void CaptureFrameIfDue()
    {
        if (!IsActive || _encoder == null || _captureRenderTexture == null)
            return;

        var encoder = _encoder;
        if (encoder.WriterFailed)
        {
            _failureReason ??= encoder.FailureReason ?? "Encoder writer reported a failure.";
            return;
        }

        var now = Time.unscaledTimeAsDouble;
        var capturesThisTick = 0;
        const int maxCapturesPerTick = 3;
        while (now >= _nextCaptureTime && capturesThisTick < maxCapturesPerTick)
        {
            if (!TryRequestReadback())
                break;

            _nextCaptureTime += _frameInterval;
            capturesThisTick++;
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
                _failureReason ??= encoder.FailureReason ?? "Encoder writer failed.";
                break;
            }

            var isNew = i < newSlots;

            var buffer = pool.Rent();
            if (buffer == null)
            {
                _droppedFrames++;
                continue;
            }

            Buffer.BlockCopy(_latestFrameBuffer!, 0, buffer, 0, _frameByteLength);

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

    public ReplayVideoCaptureResult Finalize(string endReason)
    {
        if (_finalized)
            return BuildResult(endReason);

        _finalized = true;

        try
        {
            AsyncGPUReadback.WaitAllRequests();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"AsyncGPUReadback.WaitAllRequests threw during finalize: {ex.Message}"
            );
        }

        EmitFinalFrame();

        var encoder = _encoder;
        if (encoder != null)
        {
            try
            {
                var success = encoder.WaitForCompletion(TimeSpan.FromSeconds(20));
                if (!success)
                {
                    _failureReason ??=
                        encoder.FailureReason ?? "FFmpeg failed to finalize within timeout.";
                }
            }
            catch (Exception ex)
            {
                _failureReason ??= $"Encoder finalize crashed: {ex.GetType().Name} {ex.Message}";
                BppLog.Error("CombatReplayVideo", "Encoder finalize crashed.", ex);
            }
        }

        ReleaseRenderTexture();

        var result = BuildResult(endReason);
        BppLog.Info(
            "CombatReplayVideo",
            $"Replay video capture finalized status={result.Status} frames={result.CapturedFrames} repeated={_repeatedFrames} dropped={result.DroppedFrames} duration_ms={result.DurationMs} size_bytes={result.FileSizeBytes} file={result.OutputFilePath}"
        );
        return result;
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try
        {
            if (!_finalized)
            {
                _failureReason ??= "Session disposed without finalize.";
                _encoder?.Dispose();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            // The encoder (above) joins its writer thread on Dispose, so no
            // further pool.Return can run after this point; dropping the pool /
            // pacer / staging references is safe.
            _encoder = null;
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

        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            var sequenceNumber = Interlocked.Increment(ref _issuedSequence);
            Interlocked.Increment(ref _outstandingReadbackCount);
            AsyncGPUReadback.Request(rt, 0, request => OnReadbackComplete(request, sequenceNumber));
            return true;
        }
        catch (Exception ex)
        {
            _failureReason ??= $"ScreenCapture failed: {ex.GetType().Name} {ex.Message}";
            BppLog.Error(
                "CombatReplayVideo",
                "ScreenCapture.CaptureScreenshotIntoRenderTexture failed.",
                ex
            );
            return false;
        }
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request, int sequenceNumber)
    {
        Interlocked.Decrement(ref _outstandingReadbackCount);

        if (_disposed)
            return;

        if (request.hasError)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"AsyncGPUReadback request {sequenceNumber} returned with error."
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
            data.CopyTo(_latestFrameBuffer!);
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
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to copy readback {sequenceNumber}: {ex.GetType().Name} {ex.Message}"
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
            _failureReason ??= encoder.FailureReason ?? "Encoder writer failed.";
            return;
        }

        var buffer = pool.Rent();
        if (buffer == null)
        {
            _droppedFrames++;
            return;
        }

        Buffer.BlockCopy(_latestFrameBuffer!, 0, buffer, 0, _frameByteLength);

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
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to release capture RenderTexture: {ex.Message}"
            );
        }
    }

    private void EnsureOutputDirectory()
    {
        var directory = Path.GetDirectoryName(_request.OutputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private ReplayVideoCaptureResult BuildResult(string endReason)
    {
        var endedAt = DateTimeOffset.UtcNow;
        var durationMs = (long)Math.Max(0, (endedAt - _startedAtUtc).TotalMilliseconds);
        var fileSize = FfmpegRawVideoEncoder.TryGetFileSize(_request.OutputFilePath);

        var status =
            _failureReason != null
                ? ReplayVideoCaptureStatus.Failed
                : (
                    _capturedFrames > 0
                        ? ReplayVideoCaptureStatus.Completed
                        : ReplayVideoCaptureStatus.Failed
                );
        var error = _failureReason;
        if (status == ReplayVideoCaptureStatus.Failed && error == null && _capturedFrames == 0)
            error = $"No frames captured before {endReason}.";

        return new ReplayVideoCaptureResult
        {
            VideoId = _request.VideoId,
            BattleId = _request.BattleId,
            Source = _request.Source,
            OutputFilePath = _request.OutputFilePath,
            Width = _request.Width,
            Height = _request.Height,
            Fps = _request.Fps,
            Codec = "libx264",
            Crf = _request.Crf,
            Preset = _request.Preset,
            StartedAtUtc = _startedAtUtc,
            EndedAtUtc = endedAt,
            DurationMs = durationMs,
            CapturedFrames = _capturedFrames,
            DroppedFrames = _droppedFrames,
            FileSizeBytes = fileSize,
            Status = status,
            Error = error,
        };
    }
}
