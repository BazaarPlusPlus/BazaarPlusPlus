#nullable enable
using System.Diagnostics;
using System.Text;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Second-pass muxer: combines the silent first-pass video with the captured audio WAV into the
/// final MP4 using <c>ffmpeg -c:v copy -c:a aac</c>. Runs entirely off the main thread and never
/// touches Unity types so it stays headless-testable. Any failure (no AAC encoder, ffmpeg error,
/// timeout, missing WAV) falls back to promoting the silent video to the final path so the
/// first-pass product is never lost.
/// </summary>
internal sealed class ReplayVideoAudioMuxer
{
    private const int MuxTimeoutMs = 60_000;

    private static readonly object s_pendingLock = new();
    private static readonly HashSet<Task> s_pendingTasks = new();

    // One-time AAC-encoder probe per resolved ffmpeg executable.
    private static readonly object s_aacProbeLock = new();
    private static readonly Dictionary<string, bool> s_aacProbeCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal enum MuxStatus
    {
        Muxed,
        FellBackToSilent,
        Failed,
    }

    internal enum MuxReasonCode
    {
        Muxed,
        CaptureFailed,
        NoAudio,
        FfmpegUnavailable,
        AacUnavailable,
        ProcessStartFailed,
        ProcessTimeout,
        NonZeroExit,
        ZeroDurationOutput,
        PromotionFailed,
        UnexpectedException,
    }

    internal readonly struct MuxResult
    {
        public readonly MuxStatus Status;
        public readonly string FinalFilePath;
        public readonly long FileSizeBytes;
        public readonly MuxReasonCode ReasonCode;
        public readonly int? ExitCode;
        public readonly string StderrTail;
        public readonly Exception? Exception;

        public MuxResult(
            MuxStatus status,
            string finalFilePath,
            long fileSizeBytes,
            MuxReasonCode reasonCode,
            int? exitCode = null,
            string? stderrTail = null,
            Exception? exception = null
        )
        {
            Status = status;
            FinalFilePath = finalFilePath;
            FileSizeBytes = fileSizeBytes;
            ReasonCode = reasonCode;
            ExitCode = exitCode;
            StderrTail = stderrTail ?? string.Empty;
            Exception = exception;
        }
    }

    /// <summary>
    /// Resolves the mux inline on an existing background task. This method never dispatches nested
    /// work, so an encoder drain and its mux remain one shutdown-tracked task.
    /// </summary>
    internal MuxResult Resolve(
        string recordingId,
        ReplayVideoCaptureStatus status,
        string tempVideoPath,
        string finalPath,
        IReadOnlyList<string> usableWavPaths,
        string? ffmpegExecutable
    )
    {
        try
        {
            if (
                TryResolveWithoutMux(
                    recordingId,
                    status,
                    tempVideoPath,
                    finalPath,
                    usableWavPaths,
                    ffmpegExecutable,
                    out var existingWavPaths,
                    out var synchronous
                )
            )
            {
                return synchronous;
            }

            return Mux(recordingId, ffmpegExecutable!, tempVideoPath, existingWavPaths, finalPath);
        }
        catch (Exception ex)
        {
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.UnexpectedException,
                exception: ex
            );
        }
    }

    private bool TryResolveWithoutMux(
        string recordingId,
        ReplayVideoCaptureStatus status,
        string tempVideoPath,
        string finalPath,
        IReadOnlyList<string> usableWavPaths,
        string? ffmpegExecutable,
        out IReadOnlyList<string> existingWavPaths,
        out MuxResult result
    )
    {
        existingWavPaths = Array.Empty<string>();
        if (status != ReplayVideoCaptureStatus.Completed)
        {
            result = DeleteTempAndReport(recordingId, tempVideoPath, usableWavPaths, finalPath);
            return true;
        }

        existingWavPaths = VideoProcessHelpers.GetExistingWavPaths(usableWavPaths);
        if (existingWavPaths.Count == 0)
        {
            result = PromoteAndReport(
                tempVideoPath,
                usableWavPaths,
                finalPath,
                MuxStatus.FellBackToSilent,
                MuxReasonCode.NoAudio
            );
            return true;
        }

        if (string.IsNullOrWhiteSpace(ffmpegExecutable))
        {
            result = PromoteAndReport(
                tempVideoPath,
                existingWavPaths,
                finalPath,
                MuxStatus.FellBackToSilent,
                MuxReasonCode.FfmpegUnavailable
            );
            return true;
        }

        if (!HasAacEncoder(ffmpegExecutable))
        {
            result = PromoteAndReport(
                tempVideoPath,
                existingWavPaths,
                finalPath,
                MuxStatus.FellBackToSilent,
                MuxReasonCode.AacUnavailable
            );
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Runs the ffmpeg mux pass. Background thread only. On success the silent temp and WAV are
    /// deleted (best-effort) and the final file is reported. On any failure the silent video is
    /// promoted to the final path so the recording is preserved.
    /// </summary>
    internal MuxResult Mux(
        string recordingId,
        string ffmpegExecutable,
        string silentVideoTempPath,
        IReadOnlyList<string> wavPaths,
        string finalPath,
        int audioBitrateKbps = 192
    )
    {
        var arguments = BuildArguments(silentVideoTempPath, wavPaths, finalPath, audioBitrateKbps);

        Process? process = null;
        var stderr = new BoundedTextTail();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
            };

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return FallBack(
                    silentVideoTempPath,
                    wavPaths,
                    finalPath,
                    MuxReasonCode.ProcessStartFailed
                );
            }

            // Drain stderr on a worker so a full pipe buffer can never deadlock WaitForExit.
            var drainThread = new Thread(() =>
            {
                try
                {
                    ReadStderr(stderr, process.StandardError);
                }
                catch
                {
                    // Process exited / stream closed; nothing to drain.
                }
            })
            {
                IsBackground = true,
                Name = "BPP.CombatReplayVideo.MuxStderr",
            };
            drainThread.Start();

            if (!process.WaitForExit(MuxTimeoutMs))
            {
                var exited = ForceKill(process);
                FinishDrainAfterTimeout(process, drainThread, exited);
                return FallBack(
                    silentVideoTempPath,
                    wavPaths,
                    finalPath,
                    MuxReasonCode.ProcessTimeout,
                    stderrTail: stderr.Value
                );
            }

            var exitCode = process.ExitCode;
            FinishDrainAfterExit(process, drainThread);
            var stderrTail = ReadStderrTail(stderr);

            if (exitCode == 0 && File.Exists(finalPath))
            {
                // ffmpeg with -shortest can exit 0 yet emit a zero-duration output
                // when the audio input is empty (e.g. a header-only WAV): it logs
                // "Output file is empty" and leaves a ~hundred-byte stub. Treat that
                // as a failure and fall back to the silent video. Validate BEFORE
                // deleting the silent temp so the good first-pass product survives.
                var mixedSize = FfmpegRawVideoEncoder.TryGetFileSize(finalPath);
                var silentSize = FfmpegRawVideoEncoder.TryGetFileSize(silentVideoTempPath);
                if (IsLikelyZeroDurationOutput(mixedSize, silentSize))
                {
                    return FallBack(
                        silentVideoTempPath,
                        wavPaths,
                        finalPath,
                        MuxReasonCode.ZeroDurationOutput,
                        exitCode: exitCode,
                        stderrTail: stderrTail
                    );
                }

#if DEBUG
                PreserveDebugAudioStems(finalPath, wavPaths);
#endif
                TryDelete(silentVideoTempPath);
                TryDelete(wavPaths);
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                    () =>
                        [
                            CombatReplayVideoLogEvents.MuxRecordingId.Bind(recordingId),
                            CombatReplayVideoLogEvents.MuxStage.Bind(
                                ReplayVideoLogStage.MuxCallback
                            ),
                            CombatReplayVideoLogEvents.MuxReasonCode.Bind(MuxReasonCode.Muxed),
                            CombatReplayVideoLogEvents.MuxPath.Bind(finalPath),
                            CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
                        ]
                );
                return new MuxResult(
                    MuxStatus.Muxed,
                    finalPath,
                    mixedSize,
                    MuxReasonCode.Muxed,
                    exitCode,
                    stderrTail
                );
            }

            return FallBack(
                silentVideoTempPath,
                wavPaths,
                finalPath,
                MuxReasonCode.NonZeroExit,
                exitCode,
                stderrTail
            );
        }
        catch (Exception ex)
        {
            return FallBack(
                silentVideoTempPath,
                wavPaths,
                finalPath,
                MuxReasonCode.UnexpectedException,
                stderrTail: stderr.Value,
                exception: ex
            );
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private MuxResult DeleteTempAndReport(
        string recordingId,
        string tempVideoPath,
        IReadOnlyList<string>? wavPaths,
        string finalPath
    )
    {
        try
        {
            if (File.Exists(tempVideoPath))
                File.Delete(tempVideoPath);
            TryDelete(wavPaths);
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                FfmpegRawVideoEncoder.TryGetFileSize(finalPath),
                MuxReasonCode.CaptureFailed
            );
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(recordingId),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.TempDelete
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(tempVideoPath),
                    ]
            );
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.CaptureFailed,
                exception: ex
            );
        }
    }

    private MuxResult PromoteAndReport(
        string silentVideoTempPath,
        IReadOnlyList<string>? wavPaths,
        string finalPath,
        MuxStatus status,
        MuxReasonCode reasonCode
    )
    {
        try
        {
            var size = PromoteSilentToFinal(silentVideoTempPath, finalPath);
            TryDelete(wavPaths);
            return new MuxResult(status, finalPath, size, reasonCode);
        }
        catch (Exception ex)
        {
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.PromotionFailed,
                exception: ex
            );
        }
    }

    private MuxResult FallBack(
        string silentVideoTempPath,
        IReadOnlyList<string>? wavPaths,
        string finalPath,
        MuxReasonCode reasonCode,
        int? exitCode = null,
        string? stderrTail = null,
        Exception? exception = null
    )
    {
        try
        {
            var size = PromoteSilentToFinal(silentVideoTempPath, finalPath);
            TryDelete(wavPaths);
            return new MuxResult(
                MuxStatus.FellBackToSilent,
                finalPath,
                size,
                reasonCode,
                exitCode,
                stderrTail,
                exception
            );
        }
        catch (Exception ex)
        {
            return new MuxResult(
                MuxStatus.Failed,
                finalPath,
                0,
                MuxReasonCode.PromotionFailed,
                exitCode,
                stderrTail,
                ex
            );
        }
    }

    private static string BuildArguments(
        string silentVideoTempPath,
        IReadOnlyList<string> wavPaths,
        string finalPath,
        int audioBitrateKbps
    )
    {
        if (wavPaths == null || wavPaths.Count == 0)
            throw new ArgumentException("At least one WAV path is required.", nameof(wavPaths));

        var bitrate = audioBitrateKbps > 0 ? audioBitrateKbps : 192;
        var sb = new StringBuilder();
        sb.Append("-hide_banner -loglevel warning -nostdin -y ");
        sb.Append("-i ").Append(VideoProcessHelpers.QuoteArg(silentVideoTempPath)).Append(' ');
        for (var i = 0; i < wavPaths.Count; i++)
            sb.Append("-i ").Append(VideoProcessHelpers.QuoteArg(wavPaths[i])).Append(' ');

        if (wavPaths.Count == 1)
        {
            sb.Append("-map 0:v:0 -map 1:a:0 ");
        }
        else
        {
            sb.Append("-filter_complex ");
            for (var i = 0; i < wavPaths.Count; i++)
                sb.Append('[').Append(i + 1).Append(":a]");
            sb.Append($"amix=inputs={wavPaths.Count}:normalize=0[aout] ");
            sb.Append("-map 0:v:0 -map \"[aout]\" ");
        }

        sb.Append("-c:v copy -c:a aac ");
        // Downmix to stereo 48 kHz so the AAC track is universally playable. WASAPI loopback captures
        // the device mix format, which can be 5.1/7.1 surround at non-standard rates, and many players
        // (incl. Windows Media Player / Photos) reject >2-channel AAC ("encoding settings not supported").
        sb.Append("-ac 2 -ar 48000 ");
        sb.Append($"-b:a {bitrate}k ");
        sb.Append("-shortest -movflags +faststart ");
        sb.Append(VideoProcessHelpers.QuoteArg(finalPath));
        return sb.ToString();
    }

    /// <summary>
    /// Heuristic guard against a zero-duration mux output. ffmpeg's <c>-shortest</c> trims the muxed
    /// file to the shortest input, so an empty audio input (e.g. a header-only WAV) makes it exit 0
    /// while emitting only a few hundred bytes of container with no media. Because the mux uses
    /// <c>-c:v copy</c>, a valid output always carries the full first-pass video payload and is at
    /// least as large as the silent input; a real but tiny recording is still bounded below by that
    /// silent size. So the output is considered zero-duration when it is empty, or when the silent
    /// input size is known and the output is less than half of it (generous slack for container /
    /// faststart differences, yet orders of magnitude above an empty stub). When the silent size is
    /// unknown (0), only a truly empty output is rejected.
    /// </summary>
    internal static bool IsLikelyZeroDurationOutput(long muxedSize, long silentSize)
    {
        if (muxedSize <= 0)
            return true;

        if (silentSize <= 0)
            return false;

        return muxedSize < silentSize / 2;
    }

    /// <summary>
    /// Promotes the silent first-pass video to the final path. Lifts the exact File.Move sequence
    /// previously inlined in <c>CombatReplayVideoRecorder.FinalizeOutputFile</c> so the logic lives
    /// once. Throws on hard IO failure (callers catch). If the temp is already gone, reports the
    /// current size of the final path (idempotent re-finalize).
    /// </summary>
    internal static long PromoteSilentToFinal(string tempPath, string finalPath)
    {
        if (!File.Exists(tempPath))
            return FfmpegRawVideoEncoder.TryGetFileSize(finalPath);

        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(tempPath, finalPath);
        return FfmpegRawVideoEncoder.TryGetFileSize(finalPath);
    }

    /// <summary>
    /// Best-effort, cached probe that the resolved ffmpeg exposes an AAC encoder. Parses
    /// <c>ffmpeg -hide_banner -encoders</c> once per executable. On probe failure assumes AAC is
    /// available so a transient probe error never silently strips audio (the mux itself still
    /// gracefully falls back if AAC is genuinely missing).
    /// </summary>
    internal static bool HasAacEncoder(string ffmpegExecutable)
    {
        if (string.IsNullOrWhiteSpace(ffmpegExecutable))
            return false;

        lock (s_aacProbeLock)
        {
            if (s_aacProbeCache.TryGetValue(ffmpegExecutable, out var cached))
                return cached;

            var hasAac = ProbeAacEncoder(ffmpegExecutable);
            s_aacProbeCache[ffmpegExecutable] = hasAac;
            return hasAac;
        }
    }

    internal static void ResetAacProbeCacheForTests()
    {
        lock (s_aacProbeLock)
        {
            s_aacProbeCache.Clear();
        }
    }

    private static bool ProbeAacEncoder(string ffmpegExecutable)
    {
        Process? process = null;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = "-hide_banner -encoders",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            if (!process.Start())
                return true; // Could not probe; do not strip audio over a launch hiccup.

            var stderr = new BoundedTextTail();
            var hasAac = false;
            var stdoutThread = StartAacProbeDrain(process.StandardOutput, value => hasAac = value);
            var stderrThread = StartProbeDrain(
                "BPP.CombatReplayVideo.ProbeStderr",
                process.StandardError,
                stderr
            );
            if (!process.WaitForExit(5000))
            {
                var exited = ForceKill(process);
                FinishProbeDrains(process, stdoutThread, stderrThread, exited);
                return true;
            }
            if (!FinishProbeDrains(process, stdoutThread, stderrThread, processExited: true))
                return true;

            // ffmpeg lists encoders as e.g. " A..... aac                  AAC (Advanced Audio Coding)".
            // Match the encoder token "aac" specifically (built-in or "libfdk_aac" both satisfy a
            // " aac " token search; libfdk_aac also contains "aac" so a substring check suffices).
            return hasAac;
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.MuxProbe),
                        CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.ProbeFailed
                        ),
                        CombatReplayVideoLogEvents.MuxPath.Bind(ffmpegExecutable),
                        CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
                    ]
            );
            return true; // Assume available on probe failure; mux still degrades gracefully.
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static readonly char[] s_whitespace = { ' ', '\t', '\r', '\f', '\v' };

    private static Thread StartProbeDrain(string name, TextReader reader, BoundedTextTail tail)
    {
        var thread = new Thread(() =>
        {
            try
            {
                ReadStderr(tail, reader);
            }
            catch
            {
                // Process exited or its streams were closed after a bounded timeout.
            }
        })
        {
            IsBackground = true,
            Name = name,
        };
        thread.Start();
        return thread;
    }

    private static Thread StartAacProbeDrain(TextReader reader, Action<bool> onCompleted)
    {
        var thread = new Thread(() =>
        {
            var hasAac = false;
            try
            {
                hasAac = ReadAacEncoderProbe(reader);
            }
            catch
            {
                // Process exited or its stream was closed after a bounded timeout.
            }
            onCompleted(hasAac);
        })
        {
            IsBackground = true,
            Name = "BPP.CombatReplayVideo.ProbeStdout",
        };
        thread.Start();
        return thread;
    }

    private static bool ReadAacEncoderProbe(TextReader reader)
    {
        const int maximumLineLength = 1024;
        var buffer = new char[256];
        var line = new StringBuilder(maximumLineLength);
        var lineOverflowed = false;
        var found = false;
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    if (!lineOverflowed && HasAacToken(line.ToString()))
                        found = true;
                    line.Clear();
                    lineOverflowed = false;
                    continue;
                }

                if (line.Length < maximumLineLength)
                    line.Append(character);
                else
                    lineOverflowed = true;
            }
        }

        if (!lineOverflowed && line.Length > 0 && HasAacToken(line.ToString()))
            found = true;
        return found;
    }

    private static bool FinishProbeDrains(
        Process process,
        Thread stdoutThread,
        Thread stderrThread,
        bool processExited
    )
    {
        if (processExited)
        {
            var stdoutCompleted = stdoutThread.Join(TimeSpan.FromMilliseconds(1000));
            var stderrCompleted = stderrThread.Join(TimeSpan.FromMilliseconds(1000));
            if (stdoutCompleted && stderrCompleted)
                return true;
        }

        try
        {
            process.StandardOutput.Close();
        }
        catch { }
        try
        {
            process.StandardError.Close();
        }
        catch { }
        try
        {
            process.Dispose();
        }
        catch { }
        stdoutThread.Join(TimeSpan.FromMilliseconds(500));
        stderrThread.Join(TimeSpan.FromMilliseconds(500));
        return false;
    }

    private static bool HasAacToken(string? encodersOutput)
    {
        if (string.IsNullOrEmpty(encodersOutput))
            return false;

        // ffmpeg lists encoders one per row as "<flags> <name> <description...>", e.g.
        // " A..... aac                  AAC (Advanced Audio Coding)". Scan each row and match the
        // NAME column so a description word never produces a false positive. The first flag column
        // is 'A' for audio encoders; require it so a stray "aac" in prose is ignored.
        var lines = encodersOutput!.Split('\n');
        foreach (var line in lines)
        {
            var parts = line.Split(s_whitespace, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var flags = parts[0];
            if (flags.Length == 0 || flags[0] != 'A')
                continue;

            var name = parts[1];
            if (
                string.Equals(name, "aac", StringComparison.Ordinal)
                || name.IndexOf("aac", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Best-effort waits for all outstanding tracked finalize/mux tasks to complete, up to
    /// <paramref name="timeout"/>. Intended for app shutdown so in-flight recordings get a chance to
    /// finish; any tasks still running continue in the background and their operation is closed by
    /// the recorder's shutdown sweep. Returns true if all pending tasks completed within the timeout.
    /// </summary>
    public static bool TryDrainPendingForShutdown(TimeSpan timeout)
    {
        Task[] pending;
        lock (s_pendingLock)
        {
            if (s_pendingTasks.Count == 0)
                return true;
            pending = new Task[s_pendingTasks.Count];
            s_pendingTasks.CopyTo(pending);
        }

        try
        {
            return Task.WaitAll(pending, timeout);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.MuxDrain),
                        CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.DrainFailed
                        ),
                        CombatReplayVideoLogEvents.MuxPath.Bind(null),
                        CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
                    ]
            );
            return false;
        }
    }

    public static int PendingTaskCount
    {
        get
        {
            lock (s_pendingLock)
            {
                return s_pendingTasks.Count;
            }
        }
    }

    internal static Task DispatchTracked(Action work)
    {
        if (work == null)
            throw new ArgumentNullException(nameof(work));

        var task = Task.Run(work);
        Track(task);
        return task;
    }

    private static void Track(Task task)
    {
        lock (s_pendingLock)
        {
            s_pendingTasks.Add(task);
        }

        // Untrack on completion regardless of outcome. ContinueWith runs on a pool thread.
        task.ContinueWith(
            static t =>
            {
                lock (s_pendingLock)
                {
                    s_pendingTasks.Remove(t);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static string ReadStderrTail(BoundedTextTail stderr) => stderr.Value;

    internal static string CollectStderrTailForTests(TextReader reader)
    {
        var tail = new BoundedTextTail();
        ReadStderr(tail, reader);
        return tail.Value;
    }

    private static void ReadStderr(BoundedTextTail tail, TextReader reader) =>
        tail.ReadFrom(reader);

    private static void FinishDrainAfterTimeout(
        Process process,
        Thread drainThread,
        bool processExited
    )
    {
        if (processExited)
        {
            FinishDrainAfterExit(process, drainThread);
            return;
        }

        try
        {
            process.StandardError.Close();
        }
        catch { }
        try
        {
            process.Dispose();
        }
        catch { }
        drainThread.Join(TimeSpan.FromMilliseconds(500));
    }

    private static void FinishDrainAfterExit(Process process, Thread drainThread)
    {
        if (drainThread.Join(TimeSpan.FromMilliseconds(1000)))
            return;

        try
        {
            process.StandardError.Close();
        }
        catch { }
        try
        {
            process.Dispose();
        }
        catch { }
        drainThread.Join(TimeSpan.FromMilliseconds(500));
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.TempDelete
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(path),
                    ]
            );
        }
    }

    private static void TryDelete(IReadOnlyList<string>? paths)
    {
        if (paths == null)
            return;

        foreach (var path in paths)
            TryDelete(path);
    }

    private static void PreserveDebugAudioStems(string finalPath, IReadOnlyList<string> wavPaths)
    {
        var targetPaths = BuildDebugStemCopyTargets(finalPath, wavPaths);
        for (var i = 0; i < wavPaths.Count; i++)
        {
            var sourcePath = wavPaths[i];
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                continue;

            var targetPath = targetPaths[i];
            try
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.Copy(sourcePath, targetPath, overwrite: true);
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                    () =>
                        [
                            CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                            CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.DebugStem),
                            CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                                ReplayVideoDiagnosticReasonCode.None
                            ),
                            CombatReplayVideoLogEvents.MuxPath.Bind(targetPath),
                            CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
                        ]
                );
            }
            catch (Exception ex)
            {
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                    ex,
                    () =>
                        [
                            CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                            CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.DebugStem),
                            CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                                ReplayVideoDiagnosticReasonCode.StemPreserveFailed
                            ),
                            CombatReplayVideoLogEvents.MuxPath.Bind(targetPath),
                            CombatReplayVideoLogEvents.MuxPendingCount.Bind(PendingTaskCount),
                        ]
                );
            }
        }
    }

    private static IReadOnlyList<string> BuildDebugStemCopyTargets(
        string finalPath,
        IReadOnlyList<string> wavPaths
    )
    {
        var targets = new List<string>(wavPaths.Count);
        var directory = Path.GetDirectoryName(finalPath);
        var finalStem = Path.GetFileNameWithoutExtension(finalPath);
        if (string.IsNullOrEmpty(finalStem))
            finalStem = "combat-replay";

        for (var i = 0; i < wavPaths.Count; i++)
        {
            var label = BuildDebugStemLabel(finalStem, wavPaths[i], i);
            var fileName = $"{finalStem}.debug.{label}.wav";
            targets.Add(
                string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName)
            );
        }

        return targets;
    }

    private static string BuildDebugStemLabel(string finalStem, string wavPath, int index)
    {
        var wavStem = Path.GetFileNameWithoutExtension(wavPath);
        if (!string.IsNullOrEmpty(wavStem))
        {
            var prefix = finalStem + ".";
            if (wavStem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = wavStem.Substring(prefix.Length);
                if (suffix.Equals("audio", StringComparison.OrdinalIgnoreCase))
                    return "audio";
                if (suffix.Equals("sfx.audio", StringComparison.OrdinalIgnoreCase))
                    return "sfx";
                if (suffix.EndsWith(".audio", StringComparison.OrdinalIgnoreCase))
                    return SanitizeDebugStemLabel(suffix.Substring(0, suffix.Length - 6));
                return SanitizeDebugStemLabel(suffix);
            }
        }

        return index == 0 ? "audio" : $"audio{index + 1}";
    }

    private static string SanitizeDebugStemLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "audio";

        var builder = new StringBuilder(label.Length);
        foreach (var ch in label)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                builder.Append(char.ToLowerInvariant(ch));
            else if (ch == '-' || ch == '_' || ch == '.')
                builder.Append(ch);
            else
                builder.Append('_');
        }

        return builder.Length == 0 ? "audio" : builder.ToString();
    }

    private static bool ForceKill(Process process)
    {
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
}
