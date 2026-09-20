#nullable enable
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal interface IReplayVideoArtifactFiles
{
    IReadOnlyList<ReplayVideoFileRecord> ListFiles(string rootDirectory);
    bool Exists(string fullPath);
    void Delete(string fullPath);
}

internal readonly record struct ReplayVideoFileRecord(
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastWriteAtUtc
);

internal sealed class ReplayVideoArtifactFiles : IReplayVideoArtifactFiles
{
    public IReadOnlyList<ReplayVideoFileRecord> ListFiles(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return Array.Empty<ReplayVideoFileRecord>();

        var files = new List<ReplayVideoFileRecord>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(Path.GetFullPath(rootDirectory));
        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                files.Add(
                    new ReplayVideoFileRecord(info.FullName, info.Length, info.LastWriteTimeUtc)
                );
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var info = new DirectoryInfo(child);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                    pendingDirectories.Push(info.FullName);
            }
        }
        return files;
    }

    public bool Exists(string fullPath) => File.Exists(fullPath);

    public void Delete(string fullPath) => File.Delete(fullPath);
}

internal readonly record struct ReplayVideoMaintenanceResult(
    int MetadataCount,
    int PresentMetadataCount,
    int MissingMetadataCount,
    int UnknownSuccessfulMp4Count,
    int TempCandidateCount,
    int TempDeletedCount,
    int DeleteFailureCount,
    int UnmanagedMetadataCount,
    int WorkUnits
);

internal sealed class ReplayVideoArtifactMaintenanceService
{
    internal static readonly TimeSpan DefaultTemporaryRetention = TimeSpan.FromDays(1);

    private readonly IReplayVideoArtifactCatalog _catalog;
    private readonly IReplayVideoArtifactFiles _files;
    private readonly string _rootDirectory;
    private readonly Func<IReadOnlyCollection<string>> _protectedPaths;

    internal ReplayVideoArtifactMaintenanceService(
        IReplayVideoArtifactCatalog catalog,
        IReplayVideoArtifactFiles files,
        string rootDirectory,
        Func<IReadOnlyCollection<string>>? protectedPaths = null
    )
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _rootDirectory = rootDirectory ?? string.Empty;
        _protectedPaths = protectedPaths ?? (() => Array.Empty<string>());
    }

    internal ReplayVideoMaintenanceResult Run(
        DateTimeOffset now,
        TimeSpan tempRetention,
        CancellationToken cancellationToken
    )
    {
        var artifacts = _catalog.ListArtifacts();
        var files = _files.ListFiles(_rootDirectory);
        var comparer = PathComparer;
        var workUnits = 0;
        var inventory = new Dictionary<string, ReplayVideoFileRecord>(comparer);
        foreach (var file in files)
        {
            workUnits++;
            inventory[Normalize(file.FullPath)] = file;
        }
        var protectedPaths = new HashSet<string>(_protectedPaths().Select(Normalize), comparer);
        var knownPaths = new HashSet<string>(comparer);
        var present = new List<ReplayVideoArtifactRecord>();
        var missing = new List<ReplayVideoArtifactRecord>();
        var unmanagedCount = 0;

        foreach (var artifact in artifacts)
        {
            workUnits++;
            cancellationToken.ThrowIfCancellationRequested();
            if (artifact.FileState == ReplayVideoFileState.Deleted)
                continue;
            if (
                !ReplayVideoManagedPath.TryResolve(
                    _rootDirectory,
                    artifact.VideoRelativePath,
                    out var fullPath
                )
            )
            {
                unmanagedCount++;
                continue;
            }

            fullPath = Normalize(fullPath);
            knownPaths.Add(fullPath);
            if (!inventory.TryGetValue(fullPath, out var file) || file.SizeBytes <= 0)
                missing.Add(artifact);
            else
                present.Add(artifact);
        }

        _catalog.ReconcileFileState(present, ReplayVideoFileState.Present, now);
        _catalog.ReconcileFileState(missing, ReplayVideoFileState.Missing, now);

        var unknownSuccessfulMp4Count = 0;
        var cutoff = now - tempRetention;
        var tempCandidates = new List<ReplayVideoFileRecord>();
        foreach (var file in files)
        {
            workUnits++;
            cancellationToken.ThrowIfCancellationRequested();
            var path = Normalize(file.FullPath);
            if (!IsManagedInventoryPath(path))
                continue;
            if (IsSuccessfulMp4(path) && !knownPaths.Contains(path))
                unknownSuccessfulMp4Count++;
            if (
                file.LastWriteAtUtc <= cutoff
                && IsTemporaryResidue(path)
                && !protectedPaths.Contains(path)
            )
                tempCandidates.Add(file);
        }
        var deleted = 0;
        var failures = 0;
        foreach (var candidate in tempCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _files.Delete(candidate.FullPath);
                deleted++;
            }
            catch
            {
                failures++;
            }
        }

        return new ReplayVideoMaintenanceResult(
            artifacts.Count,
            present.Count,
            missing.Count,
            unknownSuccessfulMp4Count,
            tempCandidates.Count,
            deleted,
            failures,
            unmanagedCount,
            workUnits
        );
    }

    private static bool IsSuccessfulMp4(string path) =>
        path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".recording.mp4", StringComparison.OrdinalIgnoreCase);

    private bool IsManagedInventoryPath(string fullPath) =>
        ReplayVideoManagedPath.TryMakeRelative(_rootDirectory, fullPath, out _);

    private static bool IsTemporaryResidue(string path)
    {
        if (
            path.EndsWith(".recording.mp4", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        )
            return true;

        return false;
    }

    private static StringComparer PathComparer =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static string Normalize(string path) => Path.GetFullPath(path);
}

internal static class ReplayVideoManagedPath
{
    internal static bool TryMakeRelative(
        string rootDirectory,
        string filePath,
        out string relativePath
    )
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(filePath);
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var prefix = root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, comparison))
                return false;
            relativePath = candidate.Substring(prefix.Length);
            return !string.IsNullOrWhiteSpace(relativePath);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryResolve(
        string rootDirectory,
        string storedRelativePath,
        out string fullPath
    )
    {
        fullPath = string.Empty;
        if (
            string.IsNullOrWhiteSpace(rootDirectory)
            || string.IsNullOrWhiteSpace(storedRelativePath)
            || Path.IsPathRooted(storedRelativePath)
        )
            return false;

        try
        {
            var root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(root, storedRelativePath));
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var prefix = root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, comparison))
                return false;
            fullPath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal static class ReplayVideoInFlightArtifacts
{
    private static readonly ConcurrentDictionary<string, string[]> s_paths = new(
        StringComparer.Ordinal
    );

    internal static IDisposable Protect(string recordingId, IEnumerable<string> paths)
    {
        var normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        s_paths[recordingId] = normalized;
        return new ReleaseLease(recordingId);
    }

    internal static IReadOnlyCollection<string> SnapshotPaths() =>
        s_paths.Values.SelectMany(paths => paths).Distinct(StringComparer.Ordinal).ToArray();

    private sealed class ReleaseLease(string recordingId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            s_paths.TryRemove(recordingId, out _);
        }
    }
}
