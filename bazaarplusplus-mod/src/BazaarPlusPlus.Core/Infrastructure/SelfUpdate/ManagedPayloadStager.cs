#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

/// <summary>The mod loader a self-update targets; it fixes where the Managed Payload lives.</summary>
public enum SelfUpdateHost
{
    /// <summary>BepInEx: <c>BepInEx/plugins/</c> under the game directory.</summary>
    Desktop,

    /// <summary>LemonLoader: <c>MelonLoader/Mods/</c> and <c>UserLibs/</c> under <c>files/</c>.</summary>
    Android,
}

/// <summary>Where self-update reads and writes, relative to a host's roots.</summary>
public static class SelfUpdatePaths
{
    /// <summary>The installed Managed Payload Manifest under the install root.</summary>
    public static string InstalledManifest(string installRoot, SelfUpdateHost host) =>
        Path.Combine(
            new[] { installRoot }.Concat(Host(host).InstalledManifestPath.Split('/')).ToArray()
        );

    /// <summary>The Data Root's <c>Updates/</c>, where staged versions wait for the Updater.</summary>
    public static string Updates(string dataRoot) =>
        Path.Combine(dataRoot, StagedManagedPayload.UpdatesDirectoryName);

    internal static ManagedPayloadHost Host(SelfUpdateHost host) =>
        host == SelfUpdateHost.Android
            ? ManagedPayloadHost.MelonLoader
            : ManagedPayloadHost.BepInEx;
}

public readonly record struct ManagedPayloadStageResult(bool Succeeded, string ReasonCode)
{
    public static ManagedPayloadStageResult Staged { get; } = new(true, "staged");

    public static ManagedPayloadStageResult Failed(string reasonCode) => new(false, reasonCode);
}

/// <summary>
/// Downloads a published Managed Payload zip, checks its size, SHA-256 and minisign signature,
/// extracts exactly the files its signed manifest names, verifies them as the Updater will, and
/// only then publishes the directory as <c>Updates/&lt;version&gt;/</c> with one rename. Nothing
/// it does touches the running install; a failure leaves no staged version behind.
/// </summary>
public sealed class ManagedPayloadStager
{
    private const string DownloadPrefix = ".download-";

    private readonly string _updatesDirectory;
    private readonly string _platformKey;
    private readonly ManagedPayloadHost _host;
    private readonly MinisignPublicKey _publicKey;

    public ManagedPayloadStager(string updatesDirectory, string platformKey, SelfUpdateHost host)
        : this(updatesDirectory, platformKey, host, ReleaseSigningKey.PublicKey) { }

    internal ManagedPayloadStager(
        string updatesDirectory,
        string platformKey,
        SelfUpdateHost host,
        MinisignPublicKey publicKey
    )
    {
        _updatesDirectory =
            updatesDirectory ?? throw new ArgumentNullException(nameof(updatesDirectory));
        _platformKey = platformKey ?? throw new ArgumentNullException(nameof(platformKey));
        _host = SelfUpdatePaths.Host(host);
        _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
    }

    /// <summary>Whether <paramref name="version"/> is staged complete and verified.</summary>
    public bool IsStaged(string version) =>
        StagedManagedPayload.TryVerify(
            Path.Combine(_updatesDirectory, version),
            _host,
            _platformKey,
            _publicKey,
            out var manifest,
            out _
        )
        && manifest!.Version == version;

    public async Task<ManagedPayloadStageResult> StageAsync(
        HttpClient httpClient,
        ManagedPayloadRelease release,
        CancellationToken cancellationToken
    )
    {
        if (httpClient == null)
            throw new ArgumentNullException(nameof(httpClient));
        if (release == null)
            throw new ArgumentNullException(nameof(release));
        if (
            !ManagedPayloadVersion.TryParse(release.Version, out _)
            || release.Version.Split('.').Length != 3
        )
            return ManagedPayloadStageResult.Failed("version_invalid");
        if (IsStaged(release.Version))
            return ManagedPayloadStageResult.Staged;

        Directory.CreateDirectory(_updatesDirectory);
        RemoveAbandonedDownloads();

        byte[] zip;
        using (
            var response = await httpClient
                .GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            if (!response.IsSuccessStatusCode)
                return ManagedPayloadStageResult.Failed("http_failure_status");
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                return ManagedPayloadStageResult.Failed("size_mismatch");
            zip = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (zip.LongLength != release.Size)
            return ManagedPayloadStageResult.Failed("size_mismatch");
        if (!string.Equals(Sha256Hex(zip), release.Sha256, StringComparison.Ordinal))
            return ManagedPayloadStageResult.Failed("sha256_mismatch");
        if (!Minisign.Verify(_publicKey, zip, release.Signature, out var signatureFailure))
            return ManagedPayloadStageResult.Failed("zip_" + signatureFailure);

        var download = Path.Combine(
            _updatesDirectory,
            DownloadPrefix + release.Version + "-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var extracted = Extract(zip, download, release.Version);
            if (!extracted.Succeeded)
                return extracted;
            if (
                !StagedManagedPayload.TryVerify(
                    download,
                    _host,
                    _platformKey,
                    _publicKey,
                    out _,
                    out var failure
                )
            )
                return ManagedPayloadStageResult.Failed(failure);

            var target = Path.Combine(_updatesDirectory, release.Version);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(download, target);
            return ManagedPayloadStageResult.Staged;
        }
        finally
        {
            if (Directory.Exists(download))
                Directory.Delete(download, recursive: true);
        }
    }

    private ManagedPayloadStageResult Extract(byte[] zip, string directory, string version)
    {
        using var archive = new ZipArchive(
            new MemoryStream(zip, writable: false),
            ZipArchiveMode.Read
        );
        var manifestEntry = archive.GetEntry(ManagedPayloadManifest.FileName);
        var signatureEntry = archive.GetEntry(ManagedPayloadManifest.SignatureFileName);
        if (manifestEntry == null || signatureEntry == null)
            return ManagedPayloadStageResult.Failed("manifest_missing");

        var manifestBytes = ReadEntry(manifestEntry);
        ManagedPayloadManifest manifest;
        try
        {
            manifest = ManagedPayloadManifest.Parse(manifestBytes);
        }
        catch (FormatException)
        {
            return ManagedPayloadStageResult.Failed("manifest_invalid");
        }
        if (manifest.Version != version)
            return ManagedPayloadStageResult.Failed("version_mismatch");

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, ManagedPayloadManifest.FileName), manifestBytes);
        File.WriteAllBytes(
            Path.Combine(directory, ManagedPayloadManifest.SignatureFileName),
            ReadEntry(signatureEntry)
        );
        // Only the paths the manifest names, each one a host path it may write: no entry name
        // in the archive ever becomes a path by itself.
        foreach (var file in manifest.Files)
        {
            if (!_host.MayWrite(file.Path))
                return ManagedPayloadStageResult.Failed("path_outside_managed_payload");
            var entry = archive.GetEntry(ManagedPayloadManifest.FilesDirectory + "/" + file.Path);
            if (entry == null || entry.Length != file.Size)
                return ManagedPayloadStageResult.Failed("file_missing");
            var destination = StagedManagedPayload.StagedFilePath(directory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, ReadEntry(entry));
        }
        return ManagedPayloadStageResult.Staged;
    }

    private void RemoveAbandonedDownloads()
    {
        foreach (var directory in Directory.GetDirectories(_updatesDirectory, DownloadPrefix + "*"))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(
            sha.ComputeHash(bytes)
                .Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
        );
    }
}
