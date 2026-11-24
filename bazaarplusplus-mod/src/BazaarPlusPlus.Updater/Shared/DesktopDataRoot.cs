#nullable enable
namespace BazaarPlusPlus.ManagedPayload;

public static class DesktopDataRoot
{
    public const string MacPlatformKey = "darwin-aarch64";
    public const string WindowsPlatformKey = "windows-x86_64";

    // Tempo replaces the build directory on update. Its library survives.
    public static string Resolve(
        string gameRoot,
        string directoryName,
        string? platformKey,
        Action<string>? warn = null
    )
    {
        var original = Path.Combine(gameRoot, directoryName);
        var build = new DirectoryInfo(gameRoot);
        var library = build.Parent;
        var tempo = platformKey switch
        {
            WindowsPlatformKey => build.Name.Equals("buildx64", StringComparison.OrdinalIgnoreCase)
                && library?.Name.Equals("game", StringComparison.OrdinalIgnoreCase) == true,
            MacPlatformKey => build.Name == "buildarm64"
                && Directory.Exists(Path.Combine(gameRoot, "TheBazaar_ARM64.app")),
            _ => false,
        };
        if (!tempo || library == null)
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
