namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>One depot file a snapshot keeps, and where it goes under the output directory.</summary>
public sealed record SnapshotFile(string DepotName, string Target);

/// <summary>
/// Chooses files from a depot listing. The directory that holds globalgamemanagers is the Data
/// directory: Select keeps the Managed directory beside it (a Game Assembly Snapshot), and
/// SelectCardArt keeps what Card Art is extracted from. No platform's install layout is named here.
/// </summary>
public static class SnapshotFiles
{
    public const string VersionFile = "globalgamemanagers";
    public const string RequiredAssembly = "Assembly-CSharp.dll";

    public static IReadOnlyList<SnapshotFile> Select(IEnumerable<string> depotFiles)
    {
        var (names, dataRoot) = Listing(depotFiles);
        var managedPrefix = $"{Join(dataRoot, "Managed")}/";
        var selected = new List<SnapshotFile>();
        foreach (var (depot, path) in names)
        {
            if (path == Join(dataRoot, VersionFile))
                selected.Add(new SnapshotFile(depot, $"Data/{VersionFile}"));
            else if (path.StartsWith(managedPrefix, StringComparison.Ordinal))
                selected.Add(
                    new SnapshotFile(depot, $"Data/Managed/{path[managedPrefix.Length..]}")
                );
        }
        if (selected.All(file => file.Target != $"Data/Managed/{RequiredAssembly}"))
            throw new InvalidDataException(
                $"The depot has no {RequiredAssembly} in the Managed directory beside {VersionFile}"
            );
        return selected;
    }

    /// <summary>
    /// Chooses the files Card Art is extracted from: the version file, the Addressables catalog
    /// in StreamingAssets, and the card and skill bundles beside it.
    /// </summary>
    public static IReadOnlyList<SnapshotFile> SelectCardArt(IEnumerable<string> depotFiles)
    {
        var (names, dataRoot) = Listing(depotFiles);
        var streaming = Join(dataRoot, "StreamingAssets");
        var selected = new List<SnapshotFile>();
        foreach (var (depot, path) in names)
        {
            if (!IsCardArtFile(path, dataRoot, streaming))
                continue;
            var relative = dataRoot.Length == 0 ? path : path[(dataRoot.Length + 1)..];
            selected.Add(new SnapshotFile(depot, $"Data/{relative}"));
        }
        if (selected.All(file => file.Target != $"Data/StreamingAssets/{CatalogFile}"))
            throw new InvalidDataException($"The depot has no StreamingAssets/{CatalogFile}");
        if (selected.All(file => !file.Target.EndsWith(".bundle", StringComparison.Ordinal)))
            throw new InvalidDataException("The depot has no card or skill bundle");
        return selected;
    }

    public const string CatalogFile = "aa/catalog.bin";

    private static bool IsCardArtFile(string path, string dataRoot, string streaming)
    {
        if (path == Join(dataRoot, VersionFile))
            return true;
        if (path == Join(streaming, CatalogFile))
            return true;
        var aa = $"{Join(streaming, "aa")}/";
        if (!path.StartsWith(aa, StringComparison.Ordinal))
            return false;
        var rest = path[aa.Length..].Split('/');
        if (rest.Length != 2)
            return false;
        var name = rest[1];
        return name == "skills_assets_all.bundle"
            || (
                name.StartsWith("card_", StringComparison.Ordinal)
                && name.EndsWith("_assets_all.bundle", StringComparison.Ordinal)
                && name != "card_vfx_assets_all.bundle"
            );
    }

    private static (List<(string Depot, string Path)> Names, string DataRoot) Listing(
        IEnumerable<string> depotFiles
    )
    {
        var names = depotFiles.Select(name => (Depot: name, Path: Normalize(name))).ToList();
        var dataRoots = names
            .Where(name => LastSegment(name.Path) == VersionFile)
            .Select(name => Parent(name.Path))
            .ToList();
        if (dataRoots.Count != 1)
            throw new InvalidDataException(
                $"Expected exactly one {VersionFile} in the depot, found {dataRoots.Count}"
            );
        return (names, dataRoots[0]);
    }

    private static string Normalize(string name)
    {
        var path = name.Replace('\\', '/');
        var segments = path.Split('/');
        if (
            path.Length == 0
            || path.Contains('\0')
            || segments.Any(segment =>
                segment.Length == 0 || segment == "." || segment == ".." || segment.Contains(':')
            )
        )
            throw new InvalidDataException("The depot manifest has an unsafe file path");
        return path;
    }

    private static string LastSegment(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static string Join(string directory, string name) =>
        directory.Length == 0 ? name : $"{directory}/{name}";
}
