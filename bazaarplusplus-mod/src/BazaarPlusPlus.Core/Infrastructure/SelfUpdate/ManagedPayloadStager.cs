#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using BazaarPlusPlus.ManagedPayload;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

public enum SelfUpdateHost
{
    Desktop,

    Android,
}

public static class SelfUpdatePaths
{
    public static string InstalledManifest(string installRoot, SelfUpdateHost host) =>
        Path.Combine(
            new[] { installRoot }.Concat(Host(host).InstalledManifestPath.Split('/')).ToArray()
        );

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

// Publish by rename only after verification so the Updater can never observe a partial payload.
public sealed class ManagedPayloadStager
{
    private const string DownloadPrefix = ".download-";

    private readonly string _updatesDirectory;
    private readonly string _platformKey;
    private readonly ManagedPayloadHost _host;
    private readonly MinisignPublicKey _publicKey;
    private string? _verifiedVersion;

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

    // Only this process and the next startup's Updater write Updates, so verification is cached per session.
    // If a staged copy is lost meanwhile, the Updater revalidates it and the next session can stage it again.
    public bool IsStaged(string version)
    {
        if (Volatile.Read(ref _verifiedVersion) == version)
            return true;
        var verified =
            StagedManagedPayload.TryVerify(
                Path.Combine(_updatesDirectory, version),
                _host,
                _platformKey,
                _publicKey,
                out var manifest,
                out _
            )
            && manifest!.Version == version;
        if (verified)
            Volatile.Write(ref _verifiedVersion, version);
        return verified;
    }

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

        if (release.Size <= 0 || release.Size > int.MaxValue)
            return ManagedPayloadStageResult.Failed("size_mismatch");

        Directory.CreateDirectory(_updatesDirectory);
        RemoveAbandonedDownloads();

        byte[]? zip;
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
            zip = await ReadAtMostAsync(response.Content, release.Size, cancellationToken)
                .ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (zip == null || zip.LongLength != release.Size)
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
            Volatile.Write(ref _verifiedVersion, release.Version);
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
        // Only signed, host-validated manifest paths may become destination paths. ZIP entry names are untrusted.
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

    private static async Task<byte[]?> ReadAtMostAsync(
        HttpContent content,
        long limit,
        CancellationToken cancellationToken
    )
    {
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (
            (
                read = await stream
                    .ReadAsync(chunk, 0, chunk.Length, cancellationToken)
                    .ConfigureAwait(false)
            ) > 0
        )
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
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
