#nullable enable
using System.Globalization;
using System.Text;

namespace BazaarPlusPlus.ManagedPayload;

/// <summary>One file of a Managed Payload: its install path, size, and SHA-256.</summary>
internal sealed class ManagedPayloadFile
{
    public ManagedPayloadFile(string path, long size, string sha256)
    {
        Path = path;
        Size = size;
        Sha256 = sha256;
    }

    public string Path { get; }
    public long Size { get; }
    public string Sha256 { get; }
}

/// <summary>
/// The Managed Payload Manifest (CONTEXT.md): the release build's signed list of every managed
/// and history-contract file one product version installs on one platform, with the two facts
/// self-update compares against the running copy. The release coordinator writes it
/// (release/managed-payload.mjs); the installed copy sits beside the mod as
/// <see cref="ManagedPayloadHost.InstalledManifestPath"/>, the Managed Payload zip carries it
/// with its signature at the root. Every reader, the installer's Repair among them, is a
/// consumer in the root AGENTS.md Contracts table. A line format, so the dependency-free
/// Updater parses it without a JSON library:
/// <code>
/// bpp-managed-payload-manifest 1
/// version 6.1.0
/// platform darwin-aarch64
/// history-database-user-version 1
/// non-managed-payload-digest &lt;sha256&gt;
/// file &lt;sha256&gt; &lt;size&gt; &lt;install path&gt;
/// </code>
/// </summary>
internal sealed class ManagedPayloadManifest
{
    public const string Header = "bpp-managed-payload-manifest 1";

    /// <summary>The manifest inside a Managed Payload zip and a staged update directory.</summary>
    public const string FileName = "manifest.txt";

    /// <summary>The manifest's minisign signature beside it.</summary>
    public const string SignatureFileName = "manifest.txt.minisig";

    /// <summary>The directory that holds the payload files under their install paths.</summary>
    public const string FilesDirectory = "files";

    private ManagedPayloadManifest(
        string version,
        string platform,
        int historyDatabaseUserVersion,
        string nonManagedPayloadDigest,
        IReadOnlyList<ManagedPayloadFile> files
    )
    {
        Version = version;
        Platform = platform;
        HistoryDatabaseUserVersion = historyDatabaseUserVersion;
        NonManagedPayloadDigest = nonManagedPayloadDigest;
        Files = files;
    }

    public string Version { get; }
    public string Platform { get; }
    public int HistoryDatabaseUserVersion { get; }
    public string NonManagedPayloadDigest { get; }
    public IReadOnlyList<ManagedPayloadFile> Files { get; }

    public static ManagedPayloadManifest Parse(byte[] bytes) =>
        Parse(new UTF8Encoding(false, true).GetString(bytes ?? Array.Empty<byte>()));

    public static ManagedPayloadManifest Parse(string text)
    {
        if (text == null || !text.EndsWith("\n", StringComparison.Ordinal) || text.Contains("\r"))
            throw new FormatException("A Managed Payload Manifest is LF-terminated lines.");
        var lines = text.Substring(0, text.Length - 1).Split('\n');
        if (lines.Length < 5 || lines[0] != Header)
            throw new FormatException("Not a Managed Payload Manifest.");

        var version = Field(lines[1], "version");
        if (!ManagedPayloadVersion.TryParse(version, out _) || version.Split('.').Length != 3)
            throw new FormatException($"Invalid manifest version '{version}'.");
        var platform = Field(lines[2], "platform");
        if (
            platform.Length == 0
            || platform.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
        )
            throw new FormatException($"Invalid manifest platform '{platform}'.");
        if (
            !int.TryParse(
                Field(lines[3], "history-database-user-version"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var historyVersion
            )
            || historyVersion <= 0
        )
            throw new FormatException("Invalid history-database-user-version.");
        var digest = Field(lines[4], "non-managed-payload-digest");
        if (!IsSha256(digest))
            throw new FormatException("Invalid non-managed-payload-digest.");

        var files = new List<ManagedPayloadFile>();
        string? previous = null;
        for (var i = 5; i < lines.Length; i++)
        {
            var parts = lines[i].Split(new[] { ' ' }, 4);
            if (
                parts.Length != 4
                || parts[0] != "file"
                || !IsSha256(parts[1])
                || !long.TryParse(
                    parts[2],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var size
                )
            )
                throw new FormatException($"Invalid manifest line {i + 1}.");
            var path = parts[3];
            if (!ManagedPayloadPaths.IsSafeRelative(path))
                throw new FormatException($"Unsafe manifest path '{path}'.");
            if (previous != null && string.CompareOrdinal(previous, path) >= 0)
                throw new FormatException("Manifest files must be sorted and unique.");
            previous = path;
            files.Add(new ManagedPayloadFile(path, size, parts[1]));
        }
        if (files.Count == 0)
            throw new FormatException("A Managed Payload Manifest names at least one file.");

        return new ManagedPayloadManifest(version, platform, historyVersion, digest, files);
    }

    private static string Field(string line, string name)
    {
        var prefix = name + " ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            throw new FormatException($"Expected manifest field '{name}'.");
        return line.Substring(prefix.Length);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}

/// <summary>Product versions as the payload compares them: the leading major.minor.patch.</summary>
internal static class ManagedPayloadVersion
{
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value!.Trim().Split('.');
        if (parts.Length < 3)
            return false;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (
                parts[i].Length == 0
                || (parts[i].Length > 1 && parts[i][0] == '0')
                || !int.TryParse(
                    parts[i],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[i]
                )
            )
                return false;
        }
        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}

/// <summary>Install-path rules shared by the Updater and Core's staging.</summary>
internal static class ManagedPayloadPaths
{
    public static bool IsSafeRelative(string path) =>
        !string.IsNullOrEmpty(path)
        && path.IndexOfAny(new[] { '\\', ':', '\0' }) < 0
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
}

/// <summary>
/// Where a host keeps the Managed Payload: the install-relative directories self-update may
/// write (release/payload.json producers managed and history-contract) and the installed
/// manifest beside the mod.
/// </summary>
internal sealed class ManagedPayloadHost
{
    public const string InstalledManifestFileName = "BazaarPlusPlus.managed-manifest.txt";

    public static readonly ManagedPayloadHost BepInEx = new(
        "BepInEx/plugins/",
        new[] { "BepInEx/plugins/" }
    );

    public static readonly ManagedPayloadHost MelonLoader = new(
        "MelonLoader/Mods/",
        new[] { "MelonLoader/Mods/", "MelonLoader/UserLibs/" }
    );

    private ManagedPayloadHost(string modDirectory, IReadOnlyList<string> writableDirectories)
    {
        ModDirectory = modDirectory;
        WritableDirectories = writableDirectories;
    }

    /// <summary>The install-relative directory of the mod's own assemblies.</summary>
    public string ModDirectory { get; }

    public IReadOnlyList<string> WritableDirectories { get; }

    public string InstalledManifestPath => ModDirectory + InstalledManifestFileName;

    public string VersionFilePath => ModDirectory + "BazaarPlusPlus.version";

    /// <summary>A path directly inside one of the writable directories.</summary>
    public bool MayWrite(string path) =>
        ManagedPayloadPaths.IsSafeRelative(path)
        && WritableDirectories.Any(directory =>
            path.StartsWith(directory, StringComparison.Ordinal)
            && path.IndexOf('/', directory.Length) < 0
        );
}
