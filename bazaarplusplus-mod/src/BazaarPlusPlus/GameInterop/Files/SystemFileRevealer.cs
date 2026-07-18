#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.GameInterop.Files;

internal enum SystemFileRevealPlatform
{
    MacOS,
    Windows,
    Linux,
}

internal readonly record struct SystemFileRevealCommand(string FileName, string Arguments);

internal static class SystemFileRevealer
{
    internal static bool TryReveal(string filePath, out string reason)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            reason = "Video file path is unavailable.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }

        if (!File.Exists(fullPath))
        {
            reason = "Video file no longer exists.";
            return false;
        }

        try
        {
            var command = BuildCommand(DetectPlatform(), fullPath);
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = command.FileName,
                    Arguments = command.Arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal static SystemFileRevealCommand BuildCommand(
        SystemFileRevealPlatform platform,
        string fullPath
    ) =>
        platform switch
        {
            SystemFileRevealPlatform.MacOS => new("/usr/bin/open", $"-R {Quote(fullPath)}"),
            SystemFileRevealPlatform.Windows => new("explorer.exe", $"/select,{Quote(fullPath)}"),
            _ => new("xdg-open", Quote(Path.GetDirectoryName(fullPath) ?? fullPath)),
        };

    private static SystemFileRevealPlatform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return SystemFileRevealPlatform.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SystemFileRevealPlatform.Windows;
        return SystemFileRevealPlatform.Linux;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
