#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class FfmpegLocator
{
    private static readonly object SyncRoot = new();
    private static bool _resolved;
    private static string? _resolvedPath;
    private static Exception? _lastProbeException;

    private enum ProbeSource
    {
        Bundled,
        Path,
        None,
    }

    private enum ProbeReasonCode
    {
        Available,
        NotFound,
        ProbeFailed,
        ProbeTimeout,
    }

    public static string? Resolve(string? pluginsDirectoryPath)
    {
        lock (SyncRoot)
        {
            if (_resolved)
                return _resolvedPath;

            var stopwatch = Stopwatch.StartNew();
            _lastProbeException = null;
            var source = ProbeSource.None;
            var reason = ProbeReasonCode.NotFound;
            _resolvedPath = TryResolveBundled(pluginsDirectoryPath);
            if (!string.IsNullOrWhiteSpace(_resolvedPath))
                source = ProbeSource.Bundled;
            else
            {
                _resolvedPath = TryResolveOnPath();
                if (!string.IsNullOrWhiteSpace(_resolvedPath))
                    source = ProbeSource.Path;
            }
            if (!string.IsNullOrWhiteSpace(_resolvedPath))
                reason = ProbeReasonCode.Available;
            else if (_lastProbeException is TimeoutException)
                reason = ProbeReasonCode.ProbeTimeout;
            else if (_lastProbeException != null)
                reason = ProbeReasonCode.ProbeFailed;
            _resolved = true;
            stopwatch.Stop();
            if (_lastProbeException == null)
            {
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.FfmpegProbeCompleted,
                    () =>
                        [
                            CombatReplayVideoLogEvents.FfmpegProbeAvailable.Bind(
                                !string.IsNullOrWhiteSpace(_resolvedPath)
                            ),
                            CombatReplayVideoLogEvents.FfmpegProbeSource.Bind(source),
                            CombatReplayVideoLogEvents.FfmpegProbeExecutable.Bind(_resolvedPath),
                            CombatReplayVideoLogEvents.FfmpegProbeReasonCode.Bind(reason),
                            CombatReplayVideoLogEvents.FfmpegProbeDurationMs.Bind(
                                stopwatch.ElapsedMilliseconds
                            ),
                        ]
                );
            }
            else
            {
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.FfmpegProbeCompleted,
                    _lastProbeException,
                    () =>
                        [
                            CombatReplayVideoLogEvents.FfmpegProbeAvailable.Bind(false),
                            CombatReplayVideoLogEvents.FfmpegProbeSource.Bind(source),
                            CombatReplayVideoLogEvents.FfmpegProbeExecutable.Bind(_resolvedPath),
                            CombatReplayVideoLogEvents.FfmpegProbeReasonCode.Bind(reason),
                            CombatReplayVideoLogEvents.FfmpegProbeDurationMs.Bind(
                                stopwatch.ElapsedMilliseconds
                            ),
                        ]
                );
            }

            return _resolvedPath;
        }
    }

    public static void ResetForTests()
    {
        lock (SyncRoot)
        {
            _resolved = false;
            _resolvedPath = null;
            _lastProbeException = null;
        }
    }

    private static string? TryResolveBundled(string? pluginsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(pluginsDirectoryPath))
            return null;

        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidate = Path.Combine(pluginsDirectoryPath, fileName);
        if (!File.Exists(candidate))
            return null;

        // The mod payload extraction does not set a POSIX executable bit, so on
        // non-Windows make the bundled binary executable before probing it. This
        // keeps macOS support self-contained and independent of the build machine
        // or the installer's extraction behavior.
        if (!OperatingSystem.IsWindows())
            TryMakeExecutable(candidate);

        return TryProbe(candidate) ? candidate : null;
    }

    private static string? TryResolveOnPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return TryProbe(fileName) ? fileName : null;
    }

    private static void TryMakeExecutable(string path)
    {
        // File.SetUnixFileMode does not exist on netstandard2.1 / Unity Mono, so
        // shell out to chmod. Best-effort: swallow failures and still probe.
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"0755 \"{path}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
                return;

            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch (Exception ex)
        {
            _lastProbeException = ex;
        }
    }

    private static bool TryProbe(string executable)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
                return false;

            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignore
                }

                _lastProbeException = new TimeoutException("FFmpeg probe timed out.");
                return false;
            }

            var succeeded = process.ExitCode == 0;
            if (succeeded)
                _lastProbeException = null;
            return succeeded;
        }
        catch (Exception ex)
        {
            _lastProbeException = ex;
            return false;
        }
    }

    private static class OperatingSystem
    {
        public static bool IsWindows() =>
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows
            );
    }
}
