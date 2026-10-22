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

    public static string DataRoot(string melonBaseDirectory) =>
        Path.Combine(InstallRoot(melonBaseDirectory), PathConstants.DataRootDirectoryName);

    /// <summary>
    /// The app's <c>files/</c> directory, which the MelonLoader Payload paths are relative to:
    /// the install root of self-update's installed manifest.
    /// </summary>
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
