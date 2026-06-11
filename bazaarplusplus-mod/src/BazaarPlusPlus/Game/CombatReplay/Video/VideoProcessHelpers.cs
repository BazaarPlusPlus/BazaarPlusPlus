#nullable enable
using System.Collections.Generic;
using System.IO;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class VideoProcessHelpers
{
    // Quoting is load-bearing for ffmpeg argument strings: quote only when the value
    // contains a space or a double quote.
    public static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (!value.Contains(' ') && !value.Contains('"'))
            return value;

        var escaped = value.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    public static List<string> GetExistingWavPaths(IReadOnlyList<string>? wavPaths)
    {
        var existing = new List<string>();
        if (wavPaths == null)
            return existing;

        foreach (var wavPath in wavPaths)
        {
            if (!string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath))
                existing.Add(wavPath);
        }

        return existing;
    }
}
