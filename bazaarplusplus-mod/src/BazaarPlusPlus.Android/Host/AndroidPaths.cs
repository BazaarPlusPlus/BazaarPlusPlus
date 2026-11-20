using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Android.Host;

internal static class AndroidPaths
{
    public static IPathProvider Provider(string dataRoot) => new FixedDataRoot(dataRoot);

    public static string DataRoot(string melonBaseDirectory) =>
        Path.Combine(InstallRoot(melonBaseDirectory), PathConstants.DataRootDirectoryName);

    public static string InstallRoot(string melonBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(melonBaseDirectory);
        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(melonBaseDirectory))
            ?? throw new ArgumentException(
                $"MelonLoader base directory '{melonBaseDirectory}' has no parent.",
                nameof(melonBaseDirectory)
            );
    }

    private sealed class FixedDataRoot(string dataRoot) : IPathProvider
    {
        public string? DataRootDirectoryPath { get; } = dataRoot;
    }
}
