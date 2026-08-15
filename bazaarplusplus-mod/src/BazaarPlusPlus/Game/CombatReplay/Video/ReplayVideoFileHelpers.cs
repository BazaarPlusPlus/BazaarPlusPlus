#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayVideoFileHelpers
{
    internal static List<string> GetExistingWavPaths(IReadOnlyList<string>? wavPaths)
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

    internal static long TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
