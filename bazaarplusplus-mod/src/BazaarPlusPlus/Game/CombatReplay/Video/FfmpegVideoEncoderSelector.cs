#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum FfmpegVideoEncoderProbeReasonCode
{
    Available,
    NonZeroExit,
    Timeout,
    StartFailed,
    WriteFailed,
    EmptyOutput,
    UnexpectedException,
}

internal static class FfmpegVideoEncoderSelector
{
    internal const int ProbeTimeoutMilliseconds = 4000;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, Task<FfmpegVideoEncoderProfile>> Cache = new(
        StringComparer.Ordinal
    );
    private static readonly Dictionary<string, string> TestCache = new(StringComparer.Ordinal);

    internal static FfmpegVideoEncoderProfile SelectOrPrewarm(
        string ffmpegExecutable,
        string outputDirectoryPath,
        int width,
        int height,
        int fps
    )
    {
        var task = GetOrStart(ffmpegExecutable, outputDirectoryPath, width, height, fps);
        return task.Status == TaskStatus.RanToCompletion
            ? task.Result
            : FfmpegVideoEncoderProfile.Libx264();
    }

    internal static FfmpegVideoEncoderProfile Prewarm(
        string ffmpegExecutable,
        string outputDirectoryPath,
        int width,
        int height,
        int fps
    ) =>
        GetOrStart(ffmpegExecutable, outputDirectoryPath, width, height, fps)
            .GetAwaiter()
            .GetResult();

    internal static void Invalidate(
        string ffmpegExecutable,
        int width,
        int height,
        int fps,
        string codec
    )
    {
        var key = BuildCacheKey(ffmpegExecutable, width, height, fps);
        lock (SyncRoot)
        {
            if (!Cache.TryGetValue(key, out var task))
                return;
            if (
                task.Status == TaskStatus.RanToCompletion
                && string.Equals(task.Result.Codec, codec, StringComparison.Ordinal)
            )
            {
                Cache.Remove(key);
            }
        }
    }

    internal static string[] CandidateCodecsForTests(VideoEncoderPlatform platform) =>
        platform switch
        {
            VideoEncoderPlatform.MacOS => ["h264_videotoolbox", "libx264"],
            VideoEncoderPlatform.Windows => ["h264_nvenc", "h264_qsv", "h264_amf", "libx264"],
            _ => ["libx264"],
        };

    internal static string ResolveCodecForTests(
        string cacheKey,
        VideoEncoderPlatform platform,
        Func<string, bool> probe
    )
    {
        lock (SyncRoot)
        {
            if (TestCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var codecs = CandidateCodecsForTests(platform);
        var selected = "libx264";
        for (var index = 0; index < codecs.Length - 1; index++)
        {
            if (!probe(codecs[index]))
                continue;
            selected = codecs[index];
            break;
        }

        lock (SyncRoot)
        {
            TestCache[cacheKey] = selected;
        }
        return selected;
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            Cache.Clear();
            TestCache.Clear();
        }
    }

    private static Task<FfmpegVideoEncoderProfile> GetOrStart(
        string ffmpegExecutable,
        string outputDirectoryPath,
        int width,
        int height,
        int fps
    )
    {
        var key = BuildCacheKey(ffmpegExecutable, width, height, fps);
        lock (SyncRoot)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            var platform = FfmpegVideoEncoderProfile.DetectPlatform();
            var task = Task.Run(() =>
            {
                try
                {
                    return Resolve(
                        ffmpegExecutable,
                        outputDirectoryPath,
                        platform,
                        width,
                        height,
                        fps
                    );
                }
                catch
                {
                    return FfmpegVideoEncoderProfile.Libx264();
                }
            });
            Cache[key] = task;
            return task;
        }
    }

    private static FfmpegVideoEncoderProfile Resolve(
        string ffmpegExecutable,
        string outputDirectoryPath,
        VideoEncoderPlatform platform,
        int width,
        int height,
        int fps
    )
    {
        var candidates = FfmpegVideoEncoderProfile.Candidates(platform, width, height, fps);
        var frame = new byte[ReplayVideoBufferPlan.Create(width, height).FrameByteLength];
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var outcome = Probe(
                ffmpegExecutable,
                outputDirectoryPath,
                width,
                height,
                fps,
                candidate,
                frame
            );
            LogProbe(ffmpegExecutable, width, height, fps, candidate, outcome);
            if (outcome.Available)
                return candidate;
        }

        return FfmpegVideoEncoderProfile.Libx264();
    }

    private static FfmpegVideoEncoderProbeOutcome Probe(
        string ffmpegExecutable,
        string outputDirectoryPath,
        int width,
        int height,
        int fps,
        FfmpegVideoEncoderProfile profile,
        byte[] frame
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var outputPath = Path.Combine(
            outputDirectoryPath,
            $".bpp-ffmpeg-probe-{Guid.NewGuid():N}.mp4"
        );
        Process? process = null;
        Thread? writerThread = null;
        Thread? stderrThread = null;
        Exception? writerException = null;
        var stderr = new BoundedTextTail();

        try
        {
            Directory.CreateDirectory(outputDirectoryPath);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = FfmpegVideoEncoderArguments.Build(
                        profile,
                        width,
                        height,
                        fps,
                        outputPath,
                        frameLimit: 1
                    ),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return FfmpegVideoEncoderProbeOutcome.Failure(
                    FfmpegVideoEncoderProbeReasonCode.StartFailed,
                    stopwatch.ElapsedMilliseconds,
                    stderr.Value
                );
            }

            var startedProcess = process;
            stderrThread = new Thread(() =>
            {
                try
                {
                    stderr.ReadFrom(startedProcess.StandardError);
                }
                catch (IOException)
                {
                    // Process exited / stderr pipe closed under the reader.
                }
                catch (ObjectDisposedException)
                {
                    // Process (and its StandardError) disposed while the reader was still blocked
                    // after a probe timeout + kill-resist. The stderr tail is best-effort
                    // diagnostic, so never let it escape onto this background thread.
                }
                catch
                {
                    // Best effort: a probe reader must never crash its thread.
                }
            })
            {
                IsBackground = true,
                Name = "BPP.CombatReplayVideo.ProfileProbeStderr",
            };
            stderrThread.Start();

            writerThread = new Thread(() =>
            {
                try
                {
                    var input = startedProcess.StandardInput.BaseStream;
                    input.Write(frame, 0, frame.Length);
                    input.Flush();
                    startedProcess.StandardInput.Close();
                }
                catch (Exception ex)
                {
                    writerException = ex;
                }
            })
            {
                IsBackground = true,
                Name = "BPP.CombatReplayVideo.ProfileProbeWriter",
            };
            writerThread.Start();

            if (!process.WaitForExit(ProbeTimeoutMilliseconds))
            {
                TryKill(process);
                writerThread.Join(500);
                stderrThread.Join(500);
                return FfmpegVideoEncoderProbeOutcome.Failure(
                    FfmpegVideoEncoderProbeReasonCode.Timeout,
                    stopwatch.ElapsedMilliseconds,
                    stderr.Value,
                    new TimeoutException(
                        $"FFmpeg encoder profile probe timed out after {ProbeTimeoutMilliseconds} ms."
                    )
                );
            }

            writerThread.Join(500);
            stderrThread.Join(500);
            if (writerException != null)
            {
                return FfmpegVideoEncoderProbeOutcome.Failure(
                    FfmpegVideoEncoderProbeReasonCode.WriteFailed,
                    stopwatch.ElapsedMilliseconds,
                    stderr.Value,
                    writerException
                );
            }
            if (process.ExitCode != 0)
            {
                return FfmpegVideoEncoderProbeOutcome.Failure(
                    FfmpegVideoEncoderProbeReasonCode.NonZeroExit,
                    stopwatch.ElapsedMilliseconds,
                    stderr.Value
                );
            }
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length <= 0)
            {
                return FfmpegVideoEncoderProbeOutcome.Failure(
                    FfmpegVideoEncoderProbeReasonCode.EmptyOutput,
                    stopwatch.ElapsedMilliseconds,
                    stderr.Value
                );
            }

            return FfmpegVideoEncoderProbeOutcome.Success(
                stopwatch.ElapsedMilliseconds,
                stderr.Value
            );
        }
        catch (Exception ex)
        {
            return FfmpegVideoEncoderProbeOutcome.Failure(
                FfmpegVideoEncoderProbeReasonCode.UnexpectedException,
                stopwatch.ElapsedMilliseconds,
                stderr.Value,
                ex
            );
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        TryKill(process);
                }
                catch
                {
                    // The process may not have reached Start successfully.
                }
            }
            writerThread?.Join(500);
            stderrThread?.Join(500);
            process?.Dispose();
            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
                // Probe artifacts are hidden, uniquely named, and best-effort cleaned.
            }
        }
    }

    private static void LogProbe(
        string executable,
        int width,
        int height,
        int fps,
        FfmpegVideoEncoderProfile profile,
        FfmpegVideoEncoderProbeOutcome outcome
    )
    {
        Func<BppLogFieldValue[]> values = () =>
            [
                CombatReplayVideoLogEvents.FfmpegProbeAvailable.Bind(outcome.Available),
                CombatReplayVideoLogEvents.FfmpegProbeSource.Bind("encoder-profile"),
                CombatReplayVideoLogEvents.FfmpegProbeExecutable.Bind(executable),
                CombatReplayVideoLogEvents.FfmpegProbeReasonCode.Bind(outcome.ReasonCode),
                CombatReplayVideoLogEvents.FfmpegProbeDurationMs.Bind(outcome.DurationMs),
                CombatReplayVideoLogEvents.FfmpegProbeCodec.Bind(profile.Codec),
                CombatReplayVideoLogEvents.FfmpegProbeWidth.Bind(width),
                CombatReplayVideoLogEvents.FfmpegProbeHeight.Bind(height),
                CombatReplayVideoLogEvents.FfmpegProbeFps.Bind(fps),
                CombatReplayVideoLogEvents.FfmpegProbeStderrTail.Bind(outcome.StderrTail),
            ];

        if (outcome.Exception == null)
            BppLog.DebugEvent(CombatReplayVideoLogEvents.FfmpegProbeCompleted, values);
        else
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.FfmpegProbeCompleted,
                outcome.Exception,
                values
            );
    }

    private static string BuildCacheKey(string executable, int width, int height, int fps)
    {
        var identity = executable;
        try
        {
            if (File.Exists(executable))
            {
                var file = new FileInfo(executable);
                identity = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            }
        }
        catch
        {
            // Fall back to the configured executable string.
        }

        return $"profile-v1|{FfmpegVideoEncoderProfile.DetectPlatform()}|{identity}|{width}x{height}@{fps}";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // best effort
        }

        try
        {
            process.WaitForExit(500);
        }
        catch
        {
            // best effort
        }
    }

    private readonly record struct FfmpegVideoEncoderProbeOutcome(
        bool Available,
        FfmpegVideoEncoderProbeReasonCode ReasonCode,
        long DurationMs,
        string StderrTail,
        Exception? Exception
    )
    {
        internal static FfmpegVideoEncoderProbeOutcome Success(
            long durationMs,
            string stderrTail
        ) => new(true, FfmpegVideoEncoderProbeReasonCode.Available, durationMs, stderrTail, null);

        internal static FfmpegVideoEncoderProbeOutcome Failure(
            FfmpegVideoEncoderProbeReasonCode reasonCode,
            long durationMs,
            string stderrTail,
            Exception? exception = null
        ) => new(false, reasonCode, durationMs, stderrTail, exception);
    }
}
