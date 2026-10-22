using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The Android Data Root: the Data Root directory beside the MelonLoader base directory, which
/// is <c>Android/data/&lt;package&gt;/files/BazaarPlusPlusV6</c> next to
/// <c>files/MelonLoader</c>, mirroring the desktop's <c>&lt;GameRoot&gt;/BazaarPlusPlusV6</c>
/// beside <c>BepInEx/</c>. It is anchored there rather than at
/// <c>Application.persistentDataPath</c> because that location is readable with
/// <c>adb pull</c> without root; startup logs both until the two are compared on a device.
/// </summary>
internal static class AndroidPaths
{
    /// <summary>A resolved Data Root as the Storage path port.</summary>
    public static IPathProvider Provider(string dataRoot) => new FixedDataRoot(dataRoot);

    public static string DataRoot(string melonBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(melonBaseDirectory);
        var parent =
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(melonBaseDirectory))
            ?? throw new ArgumentException(
                $"MelonLoader base directory '{melonBaseDirectory}' has no parent.",
                nameof(melonBaseDirectory)
            );
        return Path.Combine(parent, PathConstants.DataRootDirectoryName);
    }

    private sealed class FixedDataRoot(string dataRoot) : IPathProvider
    {
        public string? DataRootDirectoryPath { get; } = dataRoot;
    }
}
