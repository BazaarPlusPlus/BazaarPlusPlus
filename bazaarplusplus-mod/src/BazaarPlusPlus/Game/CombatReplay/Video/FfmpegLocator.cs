#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class FfmpegLocator
{
    private static readonly object SyncRoot = new();
    private static bool _resolved;
    private static string? _resolvedPath;

    public static string? Resolve(string? pluginsDirectoryPath)
    {
        lock (SyncRoot)
        {
            if (_resolved)
                return _resolvedPath;

            _resolvedPath = TryResolveBundled(pluginsDirectoryPath) ?? TryResolveOnPath();
            _resolved = true;

            if (string.IsNullOrEmpty(_resolvedPath))
            {
                BppLog.Info(
                    "CombatReplayVideo",
                    $"FFmpeg not detected. Drop a binary next to the mod in '{pluginsDirectoryPath}' or install it on PATH to enable replay video recording."
                );
            }
            else
            {
                BppLog.Info("CombatReplayVideo", $"FFmpeg detected: {_resolvedPath}");
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
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to set executable bit on '{path}': {ex.GetType().Name} {ex.Message}"
            );
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

                BppLog.Warn(
                    "CombatReplayVideo",
                    $"FFmpeg probe timed out: {executable}. Treating as unavailable."
                );
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"FFmpeg probe failed for '{executable}': {ex.GetType().Name} {ex.Message}"
            );
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
