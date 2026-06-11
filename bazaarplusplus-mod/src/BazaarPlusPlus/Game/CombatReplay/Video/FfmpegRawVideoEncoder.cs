#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class FfmpegRawVideoEncoder : IDisposable
{
    private readonly string _executable;
    private readonly string _outputFilePath;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly int _crf;
    private readonly string _preset;
    private readonly BlockingCollection<byte[]> _frameQueue;
    private readonly Action<byte[]>? _onFrameConsumed;
    private readonly StringBuilder _stderrBuffer = new(capacity: 2048);
    private readonly object _stderrLock = new();
    private Process? _process;
    private Thread? _writerThread;
    private Thread? _stderrThread;
    private volatile bool _running;
    private volatile bool _writerFailed;
    private volatile string? _failureReason;
    private long _bytesWritten;

    public FfmpegRawVideoEncoder(
        string executable,
        string outputFilePath,
        int width,
        int height,
        int fps,
        int crf,
        string preset,
        int maxQueuedFrames,
        Action<byte[]>? onFrameConsumed = null
    )
    {
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
        _crf = crf;
        _preset = preset;
        _frameQueue = new BlockingCollection<byte[]>(boundedCapacity: maxQueuedFrames);
        _onFrameConsumed = onFrameConsumed;
    }

    public bool IsRunning => _running && !_writerFailed;

    public bool WriterFailed => _writerFailed;

    public string? FailureReason => _failureReason;

    public int QueuedFrameCount => _frameQueue.Count;

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    public string StderrTail
    {
        get
        {
            lock (_stderrLock)
            {
                return _stderrBuffer.ToString();
            }
        }
    }

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

        BppLog.Info(
            "CombatReplayVideo",
            $"FFmpeg encoder started pid={_process.Id} {_width}x{_height}@{_fps} crf={_crf} preset={_preset} -> {_outputFilePath}"
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

    public bool WaitForCompletion(TimeSpan timeout)
    {
        SignalEndOfStream();

        var deadline = DateTime.UtcNow + timeout;

        if (_writerThread != null)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !_writerThread.Join(remaining))
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    "FFmpeg writer thread did not finish within timeout."
                );
                ForceKill();
                return false;
            }
        }

        var process = _process;
        if (process != null)
        {
            var remaining = deadline - DateTime.UtcNow;
            var waitMs = remaining > TimeSpan.Zero ? (int)remaining.TotalMilliseconds : 0;
            if (!process.WaitForExit(Math.Max(waitMs, 200)))
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    "FFmpeg did not exit within timeout; killing process."
                );
                ForceKill();
                return false;
            }

            if (process.ExitCode != 0)
            {
                _writerFailed = true;
                _failureReason ??=
                    $"ffmpeg exit code {process.ExitCode}. stderr tail: {StderrTail}";
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"FFmpeg exited with non-zero code {process.ExitCode}. stderr tail: {StderrTail}"
                );
                return false;
            }
        }

        _stderrThread?.Join(TimeSpan.FromMilliseconds(500));
        _running = false;
        return !_writerFailed;
    }

    public void Dispose()
    {
        SignalEndOfStream();
        ForceKill();

        _writerThread?.Join(TimeSpan.FromMilliseconds(500));
        _stderrThread?.Join(TimeSpan.FromMilliseconds(500));

        try
        {
            _frameQueue.Dispose();
        }
        catch
        {
            // ignore
        }

        _process?.Dispose();
        _process = null;
        _running = false;
    }

    private string BuildArguments()
    {
        var sb = new StringBuilder();
        sb.Append("-hide_banner -loglevel warning -nostdin -y ");
        sb.Append("-f rawvideo -pixel_format rgba ");
        sb.Append($"-video_size {_width}x{_height} ");
        sb.Append($"-framerate {_fps} ");
        sb.Append("-i pipe:0 ");
        sb.Append("-c:v libx264 -pix_fmt yuv420p ");
        sb.Append($"-preset {_preset} ");
        sb.Append($"-crf {_crf} ");
        sb.Append("-movflags +faststart ");
        sb.Append(VideoProcessHelpers.QuoteArg(_outputFilePath));
        return sb.ToString();
    }

    private void WriterLoop()
    {
        try
        {
            var stdin = _process?.StandardInput.BaseStream;
            if (stdin == null)
            {
                _writerFailed = true;
                _failureReason = "FFmpeg stdin stream is unavailable.";
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
                        BppLog.Debug("CombatReplayVideo", $"onFrameConsumed threw: {ex.Message}");
                    }
                }
                catch (IOException ex)
                {
                    _writerFailed = true;
                    _failureReason = $"FFmpeg stdin write failed: {ex.Message}";
                    return;
                }
                catch (ObjectDisposedException)
                {
                    _writerFailed = true;
                    _failureReason = "FFmpeg stdin was disposed.";
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
                _writerFailed = true;
                _failureReason = $"FFmpeg stdin close failed: {ex.Message}";
            }
            catch (ObjectDisposedException)
            {
                // already closed
            }
        }
        catch (Exception ex)
        {
            _writerFailed = true;
            _failureReason = $"Writer thread crashed: {ex.GetType().Name} {ex.Message}";
            BppLog.Error("CombatReplayVideo", "FFmpeg writer thread crashed.", ex);
        }
    }

    private void StderrLoop()
    {
        try
        {
            var reader = _process?.StandardError;
            if (reader == null)
                return;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                lock (_stderrLock)
                {
                    if (_stderrBuffer.Length > 4096)
                        _stderrBuffer.Remove(0, _stderrBuffer.Length - 2048);
                    _stderrBuffer.Append(line).Append('\n');
                }

                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    BppLog.Warn("CombatReplayVideo", $"ffmpeg: {line}");
                }
            }
        }
        catch (IOException)
        {
            // process exited
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"FFmpeg stderr reader exited: {ex.GetType().Name} {ex.Message}"
            );
        }
    }

    private void ForceKill()
    {
        var process = _process;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(500);
            }
        }
        catch
        {
            // ignore
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
