#nullable enable
namespace BazaarPlusPlus.ManagedPayload;

public static class DesktopDataRoot
{
    // Tempo replaces game/buildx64 on update. The library that contains it survives.
    public static string Resolve(
        string gameRoot,
        string directoryName,
        bool windows,
        Action<string>? warn = null
    )
    {
        var original = Path.Combine(gameRoot, directoryName);
        if (!windows)
            return original;
        var build = new DirectoryInfo(gameRoot);
        var library = build.Parent;
        if (
            !build.Name.Equals("buildx64", StringComparison.OrdinalIgnoreCase)
            || library == null
            || !library.Name.Equals("game", StringComparison.OrdinalIgnoreCase)
        )
            return original;

        var stable = Path.Combine(library.FullName, directoryName);
        if (Directory.Exists(stable))
            return stable;
        if (File.Exists(stable))
        {
            warn?.Invoke($"Expected a directory at {stable}. Using {original}.");
            return original;
        }
        if (Directory.Exists(original))
        {
            try
            {
                Directory.Move(original, stable);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Another host may already have completed the move.
                if (Directory.Exists(stable))
                    return stable;
                warn?.Invoke($"Could not move {original} to {stable}: {error.Message}");
                return original;
            }
        }
        return stable;
    }
}
