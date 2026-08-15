#nullable enable
namespace BazaarPlusPlus.Storage.Paths;

public static class PathProviderExtensions
{
    public static string RequireDataRoot(this IPathProvider paths)
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));
        return paths.DataRootDirectoryPath
            ?? throw new InvalidOperationException("Data root directory path is not initialized.");
    }
}
