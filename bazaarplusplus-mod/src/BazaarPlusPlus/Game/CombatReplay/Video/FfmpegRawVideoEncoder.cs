#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class FfmpegRawVideoEncoder : IDisposable
{
    private readonly string _executable;
    private readonly string _recordingId;
    private readonly string _outputFilePath;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly FfmpegVideoEncoderProfile _profile;
    private readonly BlockingCollection<byte[]> _frameQueue;
    private readonly Action<byte[]>? _onFrameConsumed;
    private readonly BoundedTextTail _stderrTail = new();
    private readonly object _failureSync = new();
    private Process? _process;
    private Thread? _writerThread;
    private Thread? _stderrThread;
    private volatile bool _running;
    private volatile bool _writerFailed;
    private volatile FfmpegEncoderFailureReasonCode _failureReasonCode;
    private Exception? _failureException;
    private long _bytesWritten;
    private int _disposeState;
    private int _disposeExecutionCount;

    public FfmpegRawVideoEncoder(
        string recordingId,
        string executable,
        string outputFilePath,
        int width,
        int height,
        int fps,
        FfmpegVideoEncoderProfile profile,
        int maxQueuedFrames,
        Action<byte[]>? onFrameConsumed = null
    )
    {
        _recordingId = recordingId ?? throw new ArgumentNullException(nameof(recordingId));
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("FFmpeg executable is required.", nameof(executable));
        if (string.IsNullOrWhiteSpace(outputFilePath))
            throw new ArgumentException("Output file path is required.", nameof(outputFilePath));
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must be positive.");
        if (fps <= 0)
            throw new ArgumentException("FPS must be positive.", nameof(fps));
        if (maxQueuedFrames <= 0)
            throw new ArgumentException(
                "Max queued frames must be positive.",
                nameof(maxQueuedFrames)
            );

        _executable = executable;
        _outputFilePath = outputFilePath;
        _width = width;
        _height = height;
        _fps = fps;
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _frameQueue = new BlockingCollection<byte[]>(boundedCapacity: maxQueuedFrames);
        _onFrameConsumed = onFrameConsumed;
    }

    public bool IsRunning => _running && !_writerFailed;

    public bool WriterFailed => _writerFailed;

    public FfmpegEncoderFailureReasonCode FailureReasonCode => _failureReasonCode;

    public int QueuedFrameCount => _frameQueue.Count;

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    public string StderrTail => _stderrTail.Value;

    internal int DisposeExecutionCount => Volatile.Read(ref _disposeExecutionCount);

    public void Start()
    {
        if (_running)
            throw new InvalidOperationException("Encoder is already running.");

        var arguments = BuildArguments();
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = startInfo };
        if (!_process.Start())
            throw new InvalidOperationException($"Failed to start FFmpeg process '{_executable}'.");

        _running = true;

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "BPP.CombatReplayVideo.FfmpegWriter",
        };
        _writerThread.Start();

        _stderrThread = new Thread(StderrLoop)
        {
            IsBackground = true,
            Name = "BPP.CombatReplayVideo.FfmpegStderr",
        };
        _stderrThread.Start();

        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.VideoCaptureStatsObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.StatsRecordingId.Bind(_recordingId),
                    CombatReplayVideoLogEvents.StatsStage.Bind(ReplayVideoLogStage.EncoderStarted),
                    CombatReplayVideoLogEvents.StatsWidth.Bind(_width),
                    CombatReplayVideoLogEvents.StatsHeight.Bind(_height),
                    CombatReplayVideoLogEvents.StatsFps.Bind(_fps),
                    CombatReplayVideoLogEvents.StatsCapturedFrames.Bind(null),
                    CombatReplayVideoLogEvents.StatsRepeatedFrames.Bind(null),
                    CombatReplayVideoLogEvents.StatsDroppedFrames.Bind(null),
                    CombatReplayVideoLogEvents.StatsDurationMs.Bind(null),
                    CombatReplayVideoLogEvents.StatsSizeBytes.Bind(null),
                    CombatReplayVideoLogEvents.StatsOutputPath.Bind(_outputFilePath),
                ]
        );
    }

    public bool TryEnqueueFrame(byte[] frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        if (!_running || _writerFailed || _frameQueue.IsAddingCompleted)
            return false;

        return _frameQueue.TryAdd(frame);
    }

    public void SignalEndOfStream()
    {
        if (!_running)
            return;

        try
        {
            if (!_frameQueue.IsAddingCompleted)
                _frameQueue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
    }

    public FfmpegEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout)
    {
        SignalEndOfStream();

        var deadline = DateTime.UtcNow + timeout;

        if (_writerThread != null)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !_writerThread.Join(remaining))
            {
                RecordFailure(FfmpegEncoderFailureReasonCode.WriterTimeout);
                var exited = ForceKill();
                FinishStderrAfterTimeout(exited);
                _running = false;
                return FfmpegEncoderCompletionOutcome.Failure(
                    FfmpegEncoderFailureReasonCode.WriterTimeout,
                    TryGetExitCode(),
                    StderrTail
                );
            }
        }

        var process = _process;
        if (process != null)
        {
            var remaining = deadline - DateTime.UtcNow;
            var waitMs = remaining > TimeSpan.Zero ? (int)remaining.TotalMilliseconds : 0;
            if (!process.WaitForExit(Math.Max(waitMs, 200)))
            {
                RecordFailure(FfmpegEncoderFailureReasonCode.ProcessTimeout);
                var exited = ForceKill();
                FinishStderrAfterTimeout(exited);
                _running = false;
                return FfmpegEncoderCompletionOutcome.Failure(
                    FfmpegEncoderFailureReasonCode.ProcessTimeout,
                    TryGetExitCode(),
                    StderrTail
                );
            }

            var exitCode = process.ExitCode;
            JoinStderrReader();

            if (exitCode != 0)
            {
                RecordFailure(FfmpegEncoderFailureReasonCode.NonZeroExit);
                return FfmpegEncoderCompletionOutcome.Failure(
                    FfmpegEncoderFailureReasonCode.NonZeroExit,
                    exitCode,
                    StderrTail
                );
            }
        }

        JoinStderrReader();
        _running = false;
        return _writerFailed
            ? FailureOutcome()
            : FfmpegEncoderCompletionOutcome.Success(StderrTail);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;
        Interlocked.Increment(ref _disposeExecutionCount);

        try
        {
            SignalEndOfStream();
        }
        catch
        {
            // best effort
        }

        ForceKill();

        try
        {
            _writerThread?.Join(TimeSpan.FromMilliseconds(500));
            _stderrThread?.Join(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // best effort
        }

        try
        {
            _frameQueue.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _process?.Dispose();
        }
        catch
        {
            // best effort
        }
        _process = null;
        _running = false;
    }

    private string BuildArguments() =>
        FfmpegVideoEncoderArguments.Build(_profile, _width, _height, _fps, _outputFilePath);

    private void WriterLoop()
    {
        try
        {
            var stdin = _process?.StandardInput.BaseStream;
            if (stdin == null)
            {
                RecordFailure(FfmpegEncoderFailureReasonCode.StdinUnavailable);
                return;
            }

            foreach (var frame in _frameQueue.GetConsumingEnumerable())
            {
                try
                {
                    stdin.Write(frame, 0, frame.Length);
                    Interlocked.Add(ref _bytesWritten, frame.Length);
                    try
                    {
                        _onFrameConsumed?.Invoke(frame);
                    }
                    catch (Exception ex)
                    {
                        BppLog.DebugEvent(
                            CombatReplayVideoLogEvents.VideoCaptureFrameDegraded,
                            ex,
                            () =>
                                [
                                    CombatReplayVideoLogEvents.FrameRecordingId.Bind(_recordingId),
                                    CombatReplayVideoLogEvents.FrameStage.Bind(
                                        ReplayVideoLogStage.FrameConsumeCallback
                                    ),
                                    CombatReplayVideoLogEvents.FrameReasonCode.Bind(
                                        ReplayVideoDiagnosticReasonCode.CallbackException
                                    ),
                                    CombatReplayVideoLogEvents.FrameSequence.Bind(null),
                                ]
                        );
                    }
                }
                catch (IOException ex)
                {
                    RecordFailure(FfmpegEncoderFailureReasonCode.StdinWriteFailed, ex);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    RecordFailure(FfmpegEncoderFailureReasonCode.StdinWriteFailed);
                    return;
                }
            }

            try
            {
                stdin.Flush();
                stdin.Close();
            }
            catch (IOException ex)
            {
                RecordFailure(FfmpegEncoderFailureReasonCode.StdinCloseFailed, ex);
            }
            catch (ObjectDisposedException)
            {
                // already closed
            }
        }
        catch (Exception ex)
        {
            RecordFailure(FfmpegEncoderFailureReasonCode.WriterCrashed, ex);
        }
    }

    private void StderrLoop()
    {
        try
        {
            var reader = _process?.StandardError;
            if (reader == null)
                return;

            ReadStderr(_stderrTail, reader);
        }
        catch (IOException)
        {
            // process exited
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoCaptureFrameDegraded,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.FrameRecordingId.Bind(_recordingId),
                        CombatReplayVideoLogEvents.FrameStage.Bind(
                            ReplayVideoLogStage.StderrReader
                        ),
                        CombatReplayVideoLogEvents.FrameReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.ReaderException
                        ),
                        CombatReplayVideoLogEvents.FrameSequence.Bind(null),
                    ]
            );
        }
    }

    internal static string CollectStderrTailForTests(TextReader reader)
    {
        var tail = new BoundedTextTail();
        ReadStderr(tail, reader);
        return tail.Value;
    }

    private static void ReadStderr(BoundedTextTail tail, TextReader reader) =>
        tail.ReadFrom(reader);

    private void JoinStderrReader()
    {
        var stderrThread = _stderrThread;
        if (stderrThread == null || ReferenceEquals(stderrThread, Thread.CurrentThread))
            return;
        if (stderrThread.Join(TimeSpan.FromMilliseconds(1000)))
            return;

        var process = _process;
        try
        {
            process?.StandardError.Close();
        }
        catch { }
        try
        {
            process?.Dispose();
        }
        catch { }
        _process = null;
        stderrThread.Join(TimeSpan.FromMilliseconds(500));
    }

    private void FinishStderrAfterTimeout(bool processExited)
    {
        if (processExited)
        {
            JoinStderrReader();
            return;
        }

        var process = _process;
        try
        {
            process?.StandardError.Close();
        }
        catch { }
        try
        {
            process?.StandardInput.Close();
        }
        catch { }
        try
        {
            process?.Dispose();
        }
        catch { }
        _process = null;
        _stderrThread?.Join(TimeSpan.FromMilliseconds(500));
    }

    private void RecordFailure(
        FfmpegEncoderFailureReasonCode reasonCode,
        Exception? exception = null
    )
    {
        _writerFailed = true;
        lock (_failureSync)
        {
            if (_failureReasonCode != FfmpegEncoderFailureReasonCode.None)
                return;
            _failureReasonCode = reasonCode;
            _failureException = exception;
        }
    }

    private FfmpegEncoderCompletionOutcome FailureOutcome()
    {
        lock (_failureSync)
        {
            return FfmpegEncoderCompletionOutcome.Failure(
                _failureReasonCode == FfmpegEncoderFailureReasonCode.None
                    ? FfmpegEncoderFailureReasonCode.WriterCrashed
                    : _failureReasonCode,
                TryGetExitCode(),
                StderrTail,
                _failureException
            );
        }
    }

    private int? TryGetExitCode()
    {
        try
        {
            return _process is { HasExited: true } process ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private bool ForceKill()
    {
        var process = _process;
        if (process == null)
            return true;

        try
        {
            if (process.HasExited)
                return true;
            process.Kill();
            return process.WaitForExit(500);
        }
        catch
        {
            try
            {
                return process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public static long TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
