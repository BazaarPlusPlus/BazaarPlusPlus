namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>One depot file a snapshot keeps, and where it goes under the output directory.</summary>
public sealed record SnapshotFile(string DepotName, string Target);

/// <summary>
/// Chooses a Game Assembly Snapshot's files from a depot listing: the directory that holds
/// globalgamemanagers gives the game version, and the Managed directory beside it gives the
/// assemblies. No platform's install layout is named here.
/// </summary>
public static class SnapshotFiles
{
    public const string VersionFile = "globalgamemanagers";
    public const string RequiredAssembly = "Assembly-CSharp.dll";

    public static IReadOnlyList<SnapshotFile> Select(IEnumerable<string> depotFiles)
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
        var managedPrefix = $"{Join(dataRoots[0], "Managed")}/";
        var selected = new List<SnapshotFile>();
        foreach (var (depot, path) in names)
        {
            if (path == Join(dataRoots[0], VersionFile))
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
