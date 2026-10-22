#nullable enable
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Updater;

/// <summary>The outcome of one Updater run.</summary>
internal enum SwapOutcome
{
    NothingStaged,
    Applied,
    Rejected,
    RolledBack,
}

/// <summary>
/// Swaps the newest verified staged Managed Payload into the install before any BPP assembly
/// loads (ADR-0014). It re-verifies the manifest signature and every file hash, moves each old
/// file aside and the staged one in under a write-ahead journal, removes the managed files the
/// installed manifest lists and the new one does not, and installs the new manifest last. A
/// failure, or a journal left by an interrupted run, rolls every step back. A staged version not
/// newer than the installed one is deleted unapplied.
/// </summary>
internal sealed class ManagedPayloadSwap
{
    private const string SwapDirectoryName = ".swap";
    private const string JournalFileName = "journal.txt";
    private const string BackupDirectoryName = "backup";

    private readonly string _installRoot;
    private readonly string _updatesDirectory;
    private readonly ManagedPayloadHost _host;
    private readonly string _platformKey;
    private readonly MinisignPublicKey _publicKey;
    private readonly UpdaterLog _log;

    public ManagedPayloadSwap(
        string installRoot,
        string dataRoot,
        ManagedPayloadHost host,
        string platformKey,
        MinisignPublicKey publicKey,
        UpdaterLog log
    )
    {
        _installRoot = installRoot ?? throw new ArgumentNullException(nameof(installRoot));
        _updatesDirectory = Path.Combine(
            dataRoot ?? throw new ArgumentNullException(nameof(dataRoot)),
            StagedManagedPayload.UpdatesDirectoryName
        );
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _platformKey = platformKey ?? throw new ArgumentNullException(nameof(platformKey));
        _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private string SwapDirectory => Path.Combine(_updatesDirectory, SwapDirectoryName);

    private string JournalPath => Path.Combine(SwapDirectory, JournalFileName);

    public SwapOutcome Run()
    {
        if (!Directory.Exists(_updatesDirectory))
            return SwapOutcome.NothingStaged;

        RecoverInterruptedSwap();

        var installed = ReadInstalled();
        var outcome = SwapOutcome.NothingStaged;
        foreach (var (version, directory) in StagedVersions())
        {
            var name = Path.GetFileName(directory);
            if (outcome == SwapOutcome.Applied || version <= installed.Version)
            {
                _log.Info(
                    "updater.staged.stale",
                    ("version", name),
                    ("installed", installed.Label)
                );
                DeleteDirectory(directory);
                continue;
            }

            if (
                !StagedManagedPayload.TryVerify(
                    directory,
                    _host,
                    _platformKey,
                    _publicKey,
                    out var manifest,
                    out var failure
                ) || !Compatible(manifest!, name, installed, out failure)
            )
            {
                _log.Warn("updater.staged.rejected", ("version", name), ("reason_code", failure));
                DeleteDirectory(directory);
                outcome = SwapOutcome.Rejected;
                continue;
            }

            outcome = Apply(directory, manifest!, installed);
            if (outcome != SwapOutcome.Applied)
                return outcome;
        }
        return outcome;
    }

    // Core's staging already refused these (SelfUpdatePolicy); the Updater holds the line again
    // for anything staged by other means. A copy without an installed manifest (a development
    // build) has nothing to compare.
    private static bool Compatible(
        ManagedPayloadManifest manifest,
        string directoryVersion,
        Installed installed,
        out string failure
    )
    {
        failure =
            manifest.Version != directoryVersion ? "version_mismatch"
            : installed.Manifest == null ? string.Empty
            : manifest.HistoryDatabaseUserVersion != installed.Manifest.HistoryDatabaseUserVersion
                ? "history_database_mismatch"
            : manifest.NonManagedPayloadDigest != installed.Manifest.NonManagedPayloadDigest
                ? "non_managed_payload_mismatch"
            : string.Empty;
        return failure.Length == 0;
    }

    private SwapOutcome Apply(
        string stagedDirectory,
        ManagedPayloadManifest manifest,
        Installed installed
    )
    {
        var incoming = new HashSet<string>(
            manifest.Files.Select(file => file.Path),
            StringComparer.Ordinal
        );
        var residue = (installed.Manifest?.Files ?? Array.Empty<ManagedPayloadFile>())
            .Select(file => file.Path)
            .Where(path => !incoming.Contains(path) && _host.MayWrite(path))
            .ToArray();

        if (Directory.Exists(SwapDirectory))
            DeleteDirectory(SwapDirectory);
        Directory.CreateDirectory(Path.Combine(SwapDirectory, BackupDirectoryName));
        var journal = new Journal(JournalPath);
        try
        {
            journal.Append("swap", manifest.Version);
            foreach (var path in residue)
                MoveAside(journal, path);
            foreach (var file in manifest.Files)
            {
                MoveAside(journal, file.Path);
                journal.Append("placed", file.Path);
                Move(
                    StagedManagedPayload.StagedFilePath(stagedDirectory, file.Path),
                    Target(file.Path)
                );
            }
            MoveAside(journal, _host.InstalledManifestPath);
            journal.Append("placed", _host.InstalledManifestPath);
            File.Copy(
                Path.Combine(stagedDirectory, ManagedPayloadManifest.FileName),
                Target(_host.InstalledManifestPath)
            );
            journal.Append("commit", manifest.Version);
        }
        catch (Exception exception)
        {
            journal.Dispose();
            _log.Error(
                "updater.swap.failed",
                exception,
                ("version", manifest.Version),
                ("installed", installed.Label)
            );
            RollBack(Journal.Read(JournalPath));
            DeleteDirectory(SwapDirectory);
            DeleteDirectory(stagedDirectory);
            return SwapOutcome.RolledBack;
        }

        journal.Dispose();
        DeleteDirectory(SwapDirectory);
        DeleteDirectory(stagedDirectory);
        _log.Info(
            "updater.swap.applied",
            ("from", installed.Label),
            ("to", manifest.Version),
            ("files", manifest.Files.Count),
            ("removed", residue.Length)
        );
        return SwapOutcome.Applied;
    }

    private void RecoverInterruptedSwap()
    {
        if (!Directory.Exists(SwapDirectory))
            return;
        var entries = File.Exists(JournalPath)
            ? Journal.Read(JournalPath)
            : new List<(string Kind, string Value)>();
        var version = entries.FirstOrDefault(entry => entry.Kind == "swap").Value ?? "unknown";
        if (entries.Any(entry => entry.Kind == "commit"))
        {
            _log.Info("updater.swap.completed_cleanup", ("version", version));
        }
        else
        {
            RollBack(entries);
            _log.Warn("updater.swap.recovered", ("version", version));
        }
        DeleteDirectory(SwapDirectory);
    }

    private void RollBack(IReadOnlyList<(string Kind, string Value)> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var (kind, path) = entries[i];
            if (kind == "placed")
            {
                if (File.Exists(Target(path)))
                    File.Delete(Target(path));
            }
            else if (kind == "aside")
            {
                var backup = Backup(path);
                if (!File.Exists(backup))
                    continue;
                if (File.Exists(Target(path)))
                    File.Delete(Target(path));
                Move(backup, Target(path));
            }
        }
    }

    private void MoveAside(Journal journal, string path)
    {
        if (!File.Exists(Target(path)))
            return;
        journal.Append("aside", path);
        Move(Target(path), Backup(path));
    }

    private static void Move(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
        File.Move(source, destination);
    }

    private string Target(string path) => Resolve(_installRoot, path);

    private string Backup(string path) =>
        Resolve(Path.Combine(SwapDirectory, BackupDirectoryName), path);

    private static string Resolve(string root, string path) =>
        Path.Combine(new[] { root }.Concat(path.Split('/')).ToArray());

    private IEnumerable<(Version Version, string Directory)> StagedVersions() =>
        Directory
            .GetDirectories(_updatesDirectory)
            .Select(directory =>
                (
                    Parsed: ManagedPayloadVersion.TryParse(
                        Path.GetFileName(directory),
                        out var version
                    )
                        && Path.GetFileName(directory).Split('.').Length == 3,
                    Version: version,
                    Directory: directory
                )
            )
            .Where(candidate => candidate.Parsed)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => (candidate.Version, candidate.Directory))
            .ToArray();

    private Installed ReadInstalled()
    {
        var manifestPath = Target(_host.InstalledManifestPath);
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = ManagedPayloadManifest.Parse(File.ReadAllBytes(manifestPath));
                ManagedPayloadVersion.TryParse(manifest.Version, out var version);
                return new Installed(version, manifest.Version, manifest);
            }
            catch (FormatException)
            {
                _log.Warn("updater.installed_manifest.invalid", ("path", manifestPath));
            }
        }

        var versionPath = Target(_host.VersionFilePath);
        if (File.Exists(versionPath))
        {
            var text = File.ReadAllText(versionPath).Trim();
            if (ManagedPayloadVersion.TryParse(text, out var version))
                return new Installed(version, text, null);
        }
        return new Installed(new Version(0, 0, 0), "none", null);
    }

    private void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Warn("updater.cleanup.failed", ("path", directory), ("error", exception.Message));
        }
    }

    private sealed class Installed
    {
        public Installed(Version version, string label, ManagedPayloadManifest? manifest)
        {
            Version = version;
            Label = label;
            Manifest = manifest;
        }

        public Version Version { get; }
        public string Label { get; }
        public ManagedPayloadManifest? Manifest { get; }
    }

    /// <summary>Write-ahead journal: each step is on disk before it happens.</summary>
    private sealed class Journal : IDisposable
    {
        private readonly FileStream _stream;
        private readonly StreamWriter _writer;

        public Journal(string path)
        {
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(_stream) { NewLine = "\n" };
        }

        public void Append(string kind, string value)
        {
            _writer.WriteLine(kind + " " + value);
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
        }

        public void Dispose() => _writer.Dispose();

        public static List<(string Kind, string Value)> Read(string path) =>
            File.ReadAllLines(path)
                .Select(line => line.Split(new[] { ' ' }, 2))
                .Where(parts => parts.Length == 2)
                .Select(parts => (parts[0], parts[1]))
                .ToList();
    }
}
