#nullable enable
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Headers;
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

public readonly record struct ManagedPayloadStageResult(
    bool Succeeded,
    string ReasonCode,
    string Mode = "full",
    long DownloadedBytes = 0,
    string? IncrementalFallbackReason = null
)
{
    public static ManagedPayloadStageResult Staged { get; } = new(true, "staged");

    public static ManagedPayloadStageResult StagedWithTelemetry(
        string mode,
        long downloadedBytes,
        string? incrementalFallbackReason = null
    ) => new(true, "staged", mode, downloadedBytes, incrementalFallbackReason);

    public static ManagedPayloadStageResult Failed(
        string reasonCode,
        string? incrementalFallbackReason = null
    ) => new(false, reasonCode, "full", 0, incrementalFallbackReason);
}

// Publish by rename only after verification so the Updater can never observe a partial payload.
public sealed class ManagedPayloadStager
{
    private const string DownloadPrefix = ".download-";
    private const string TempChunkPackName = ".chunks.tmp";
    private const int ChunkDownloadConcurrency = 8;
    private const int ChunkDownloadAttempts = 2;
    private const long MaxSeedScanBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(30);

    private readonly string _updatesDirectory;
    private readonly string _platformKey;
    private readonly ManagedPayloadHost _host;
    private readonly MinisignPublicKey _publicKey;
    private readonly string? _seedInstallRoot;
    private string? _verifiedVersion;

    public ManagedPayloadStager(
        string updatesDirectory,
        string platformKey,
        SelfUpdateHost host,
        string? seedInstallRoot = null
    )
        : this(updatesDirectory, platformKey, host, ReleaseSigningKey.PublicKey, seedInstallRoot)
    { }

    internal ManagedPayloadStager(
        string updatesDirectory,
        string platformKey,
        SelfUpdateHost host,
        MinisignPublicKey publicKey,
        string? seedInstallRoot = null
    )
    {
        _updatesDirectory =
            updatesDirectory ?? throw new ArgumentNullException(nameof(updatesDirectory));
        _platformKey = platformKey ?? throw new ArgumentNullException(nameof(platformKey));
        _host = SelfUpdatePaths.Host(host);
        _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        _seedInstallRoot = seedInstallRoot;
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

        string? incrementalFallbackReason = null;
        if (release.ChunkIndex != null && !string.IsNullOrWhiteSpace(_seedInstallRoot))
        {
            var incremental = await TryStageIncrementalAsync(
                    httpClient,
                    release,
                    release.ChunkIndex,
                    _seedInstallRoot!,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (incremental.Succeeded)
                return incremental;
            incrementalFallbackReason = incremental.ReasonCode;
        }

        return await StageFullZipAsync(
                httpClient,
                release,
                incrementalFallbackReason,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<ManagedPayloadStageResult> StageFullZipAsync(
        HttpClient httpClient,
        ManagedPayloadRelease release,
        string? incrementalFallbackReason,
        CancellationToken cancellationToken
    )
    {
        byte[]? zip;
        using (
            var response = await httpClient
                .GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            if (!response.IsSuccessStatusCode)
                return ManagedPayloadStageResult.Failed(
                    "http_failure_status",
                    incrementalFallbackReason
                );
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                return ManagedPayloadStageResult.Failed("size_mismatch", incrementalFallbackReason);
            zip = await ReadAtMostAsync(response.Content, release.Size, cancellationToken)
                .ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (zip == null || zip.LongLength != release.Size)
            return ManagedPayloadStageResult.Failed("size_mismatch", incrementalFallbackReason);
        if (!string.Equals(Sha256Hex(zip), release.Sha256, StringComparison.Ordinal))
            return ManagedPayloadStageResult.Failed("sha256_mismatch", incrementalFallbackReason);
        if (!Minisign.Verify(_publicKey, zip, release.Signature, out var signatureFailure))
            return ManagedPayloadStageResult.Failed(
                "zip_" + signatureFailure,
                incrementalFallbackReason
            );

        var download = CreateDownloadDirectory(release.Version);
        try
        {
            var extracted = Extract(zip, download, release.Version);
            if (!extracted.Succeeded)
                return ManagedPayloadStageResult.Failed(
                    extracted.ReasonCode,
                    incrementalFallbackReason
                );
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
                return ManagedPayloadStageResult.Failed(failure, incrementalFallbackReason);

            PublishDownload(download, release.Version);
            return ManagedPayloadStageResult.StagedWithTelemetry(
                "full",
                zip.LongLength,
                incrementalFallbackReason
            );
        }
        finally
        {
            TryDeleteDirectory(download);
        }
    }

    private async Task<ManagedPayloadStageResult> TryStageIncrementalAsync(
        HttpClient httpClient,
        ManagedPayloadRelease release,
        ManagedPayloadChunkIndexReference chunkIndexRef,
        string seedInstallRoot,
        CancellationToken cancellationToken
    )
    {
        if (chunkIndexRef.Size <= 0 || chunkIndexRef.Size > ManagedPayloadChunkIndex.MaxIndexBytes)
            return ManagedPayloadStageResult.Failed("chunk_index_size_invalid");

        byte[] indexBytes;
        try
        {
            var fetched = await FetchVerifiedBytesAsync(
                    httpClient,
                    chunkIndexRef.Url,
                    chunkIndexRef.Size,
                    chunkIndexRef.Sha256,
                    identityEncoding: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!fetched.Succeeded)
                return ManagedPayloadStageResult.Failed("chunk_index_" + fetched.ReasonCode);
            indexBytes = fetched.Bytes!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ManagedPayloadStageResult.Failed("chunk_index_fetch_exception");
        }

        if (
            !Minisign.Verify(
                _publicKey,
                indexBytes,
                chunkIndexRef.Signature,
                out var indexSigFailure
            )
        )
            return ManagedPayloadStageResult.Failed("chunk_index_" + indexSigFailure);

        ManagedPayloadChunkIndex index;
        try
        {
            index = ManagedPayloadChunkIndex.Parse(indexBytes);
        }
        catch (FormatException)
        {
            return ManagedPayloadStageResult.Failed("chunk_index_invalid");
        }

        if (!string.Equals(index.Version, release.Version, StringComparison.Ordinal))
            return ManagedPayloadStageResult.Failed("chunk_index_version_mismatch");
        if (!string.Equals(index.Platform, _platformKey, StringComparison.Ordinal))
            return ManagedPayloadStageResult.Failed("chunk_index_platform_mismatch");
        if (
            !Minisign.Verify(
                _publicKey,
                index.ManifestBytes,
                index.ManifestSignatureText,
                out var manifestSigFailure
            )
        )
            return ManagedPayloadStageResult.Failed("chunk_manifest_" + manifestSigFailure);

        foreach (var file in index.Files)
        {
            if (!_host.MayWrite(file.Path) || file.Path == _host.InstalledManifestPath)
                return ManagedPayloadStageResult.Failed("path_outside_managed_payload");
        }

        var neededHashes = new HashSet<string>(
            index.Chunks.Select(chunk => chunk.RawSha256),
            StringComparer.Ordinal
        );
        var localChunks = ScanLocalSeedChunks(seedInstallRoot, index.Chunking, neededHashes);

        var missing = new List<ManagedPayloadChunkRecord>();
        long missingDownloadBytes = 0;
        foreach (var chunk in index.Chunks)
        {
            if (localChunks.ContainsKey(chunk.RawSha256))
                continue;
            missing.Add(chunk);
            missingDownloadBytes += chunk.CompressedSize;
        }

        var localHits = index.Chunks.Count - missing.Count;
        if (
            missing.Count > 0
            && (
                (localHits == 0 && missing.Count > 1)
                || missingDownloadBytes * 10L >= release.Size * 9L
            )
        )
            return ManagedPayloadStageResult.Failed("threshold_exceeded");

        var download = CreateDownloadDirectory(release.Version);
        var packPath = Path.Combine(download, TempChunkPackName);
        try
        {
            Directory.CreateDirectory(download);
            var downloadedSlices = new Dictionary<string, PackSlice>(StringComparer.Ordinal);
            var packLock = new object();

            using (
                var packStream = new FileStream(
                    packPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None
                )
            )
            {
                if (missing.Count > 0)
                {
                    var downloadResult = await DownloadMissingChunksAsync(
                            httpClient,
                            chunkIndexRef.ChunksBaseUrl,
                            missing,
                            packStream,
                            packLock,
                            downloadedSlices,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    if (!downloadResult.Succeeded)
                        return downloadResult;
                }

                var reassembled = ReassembleFiles(
                    index,
                    download,
                    localChunks,
                    packStream,
                    packLock,
                    downloadedSlices
                );
                if (!reassembled.Succeeded)
                    return reassembled;
            }

            // Dispose and delete the temporary chunk pack before verification and atomic publish.
            if (File.Exists(packPath))
                File.Delete(packPath);

            File.WriteAllBytes(
                Path.Combine(download, ManagedPayloadManifest.FileName),
                index.ManifestBytes
            );
            File.WriteAllBytes(
                Path.Combine(download, ManagedPayloadManifest.SignatureFileName),
                index.ManifestSignatureBytes
            );

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

            PublishDownload(download, release.Version);
            return ManagedPayloadStageResult.StagedWithTelemetry(
                "incremental",
                indexBytes.LongLength + missingDownloadBytes
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ManagedPayloadStageResult.Failed("incremental_exception");
        }
        finally
        {
            TryDeleteDirectory(download);
        }
    }

    private Dictionary<string, LocalChunkSlice> ScanLocalSeedChunks(
        string seedInstallRoot,
        FastCdcParameters parameters,
        HashSet<string> neededHashes
    )
    {
        var result = new Dictionary<string, LocalChunkSlice>(StringComparer.Ordinal);
        if (neededHashes.Count == 0)
            return result;

        long scannedBytes = 0;
        using var sha = SHA256.Create();
        foreach (var relativeDir in _host.WritableDirectories)
        {
            var directory = Path.Combine(
                new[] { seedInstallRoot }
                    .Concat(relativeDir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    .ToArray()
            );
            if (!Directory.Exists(directory))
                continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            Array.Sort(files, StringComparer.Ordinal);
            foreach (var filePath in files)
            {
                if (result.Count == neededHashes.Count)
                    return result;
                byte[] bytes;
                try
                {
                    var info = new FileInfo(filePath);
                    if (
                        !info.Exists
                        || info.Length <= 0
                        || scannedBytes + info.Length > MaxSeedScanBytes
                    )
                        continue;
                    bytes = ReadSharedFileBytes(filePath, (int)info.Length);
                    scannedBytes += bytes.Length;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                var boundaries = FastCdcChunker.ChunkBoundaries(bytes, bytes.Length, parameters);
                var start = 0;
                foreach (var end in boundaries)
                {
                    var len = end - start;
                    var hash = Sha256Hex(sha, bytes, start, len);
                    if (neededHashes.Contains(hash) && !result.ContainsKey(hash))
                        result[hash] = new LocalChunkSlice(filePath, start, len);
                    start = end;
                }
            }
        }
        return result;
    }

    private static async Task<ManagedPayloadStageResult> DownloadMissingChunksAsync(
        HttpClient httpClient,
        Uri chunksBaseUrl,
        List<ManagedPayloadChunkRecord> missing,
        FileStream packStream,
        object packLock,
        Dictionary<string, PackSlice> downloadedSlices,
        CancellationToken cancellationToken
    )
    {
        var baseUrl = chunksBaseUrl.AbsoluteUri.TrimEnd('/');
        var queue = new ConcurrentQueue<ManagedPayloadChunkRecord>(missing);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string? firstFailure = null;
        var workerCount = Math.Min(ChunkDownloadConcurrency, missing.Count);

        var workers = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(
                async () =>
                {
                    while (!linkedCts.IsCancellationRequested && queue.TryDequeue(out var chunk))
                    {
                        var chunkUrl = new Uri(
                            baseUrl
                                + "/"
                                + chunk.CompressedSha256.Substring(0, 2)
                                + "/"
                                + chunk.CompressedSha256
                                + ".deflate",
                            UriKind.Absolute
                        );
                        var outcome = await FetchAndInflateChunkAsync(
                                httpClient,
                                chunkUrl,
                                chunk,
                                linkedCts.Token
                            )
                            .ConfigureAwait(false);
                        if (!outcome.Succeeded)
                        {
                            Interlocked.CompareExchange(ref firstFailure, outcome.ReasonCode, null);
                            linkedCts.Cancel();
                            return;
                        }

                        var raw = outcome.Bytes!;
                        lock (packLock)
                        {
                            var offset = packStream.Length;
                            packStream.Seek(offset, SeekOrigin.Begin);
                            packStream.Write(raw, 0, raw.Length);
                            downloadedSlices[chunk.RawSha256] = new PackSlice(offset, raw.Length);
                        }
                    }
                },
                CancellationToken.None
            );
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (firstFailure != null)
            return ManagedPayloadStageResult.Failed(firstFailure);
        return ManagedPayloadStageResult.Staged;
    }

    private static async Task<FetchBytesResult> FetchAndInflateChunkAsync(
        HttpClient httpClient,
        Uri url,
        ManagedPayloadChunkRecord chunk,
        CancellationToken cancellationToken
    )
    {
        string lastFailure = "chunk_fetch_failed";
        for (var attempt = 1; attempt <= ChunkDownloadAttempts; attempt++)
        {
            FetchBytesResult fetched;
            try
            {
                fetched = await FetchVerifiedBytesAsync(
                        httpClient,
                        url,
                        chunk.CompressedSize,
                        chunk.CompressedSha256,
                        identityEncoding: true,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return FetchBytesResult.Failed("cancelled");
            }
            catch (Exception)
            {
                lastFailure = "chunk_request_exception";
                if (attempt < ChunkDownloadAttempts)
                    continue;
                return FetchBytesResult.Failed(lastFailure);
            }

            if (!fetched.Succeeded)
            {
                lastFailure = "chunk_" + fetched.ReasonCode;
                if (fetched.Retryable && attempt < ChunkDownloadAttempts)
                    continue;
                return FetchBytesResult.Failed(lastFailure);
            }

            var inflated = TryInflateRaw(fetched.Bytes!, chunk.RawSize);
            if (inflated == null)
                return FetchBytesResult.Failed("chunk_inflate_failed");
            if (!string.Equals(Sha256Hex(inflated), chunk.RawSha256, StringComparison.Ordinal))
                return FetchBytesResult.Failed("chunk_raw_sha256_mismatch");
            return FetchBytesResult.Ok(inflated);
        }
        return FetchBytesResult.Failed(lastFailure);
    }

    private static ManagedPayloadStageResult ReassembleFiles(
        ManagedPayloadChunkIndex index,
        string downloadDirectory,
        Dictionary<string, LocalChunkSlice> localChunks,
        FileStream packStream,
        object packLock,
        Dictionary<string, PackSlice> downloadedSlices
    )
    {
        using var chunkSha = SHA256.Create();
        foreach (var file in index.Files)
        {
            var destination = StagedManagedPayload.StagedFilePath(downloadDirectory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            long written = 0;
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (
                var output = new FileStream(
                    destination,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                foreach (var chunkIndex in file.Chunks)
                {
                    var record = index.Chunks[chunkIndex];
                    byte[] raw;
                    if (localChunks.TryGetValue(record.RawSha256, out var local))
                    {
                        if (local.Length != record.RawSize)
                            return ManagedPayloadStageResult.Failed("local_chunk_size_mismatch");
                        try
                        {
                            raw = ReadSharedFileSlice(local.FilePath, local.Offset, local.Length);
                        }
                        catch (IOException)
                        {
                            return ManagedPayloadStageResult.Failed("local_chunk_read_failed");
                        }
                        catch (UnauthorizedAccessException)
                        {
                            return ManagedPayloadStageResult.Failed("local_chunk_read_failed");
                        }
                        if (
                            !string.Equals(
                                Sha256Hex(chunkSha, raw, 0, raw.Length),
                                record.RawSha256,
                                StringComparison.Ordinal
                            )
                        )
                            return ManagedPayloadStageResult.Failed("local_chunk_changed");
                    }
                    else if (downloadedSlices.TryGetValue(record.RawSha256, out var slice))
                    {
                        if (slice.Length != record.RawSize)
                            return ManagedPayloadStageResult.Failed(
                                "downloaded_chunk_size_mismatch"
                            );
                        raw = new byte[slice.Length];
                        lock (packLock)
                        {
                            packStream.Seek(slice.Offset, SeekOrigin.Begin);
                            if (!ReadExact(packStream, raw, 0, raw.Length))
                                return ManagedPayloadStageResult.Failed("pack_read_failed");
                        }
                    }
                    else
                    {
                        return ManagedPayloadStageResult.Failed("chunk_missing");
                    }

                    output.Write(raw, 0, raw.Length);
                    fileHash.AppendData(raw, 0, raw.Length);
                    written += raw.Length;
                }
            }

            if (written != file.Size)
                return ManagedPayloadStageResult.Failed("file_size_mismatch");
            var actualSha = ToHex(fileHash.GetHashAndReset());
            if (!string.Equals(actualSha, file.Sha256, StringComparison.Ordinal))
                return ManagedPayloadStageResult.Failed("file_hash_mismatch");
        }
        return ManagedPayloadStageResult.Staged;
    }

    private static async Task<FetchBytesResult> FetchVerifiedBytesAsync(
        HttpClient httpClient,
        Uri url,
        long expectedSize,
        string expectedSha256,
        bool identityEncoding,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerRequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (identityEncoding)
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            var retryable = code == 429 || code >= 500;
            return FetchBytesResult.Failed("http_failure_status", retryable);
        }
        if (response.Content.Headers.ContentLength is long length && length != expectedSize)
            return FetchBytesResult.Failed("size_mismatch");
        var bytes = await ReadAtMostAsync(response.Content, expectedSize, timeoutCts.Token)
            .ConfigureAwait(false);
        if (bytes == null || bytes.LongLength != expectedSize)
            return FetchBytesResult.Failed("size_mismatch");
        if (!string.Equals(Sha256Hex(bytes), expectedSha256, StringComparison.Ordinal))
            return FetchBytesResult.Failed("sha256_mismatch");
        return FetchBytesResult.Ok(bytes);
    }

    private static byte[]? TryInflateRaw(byte[] compressed, int expectedRawSize)
    {
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            var output = new byte[expectedRawSize];
            var total = 0;
            while (total < expectedRawSize)
            {
                var read = deflate.Read(output, total, expectedRawSize - total);
                if (read == 0)
                    return null;
                total += read;
            }
            // Reject streams that expand past expectedRawSize (decompression bomb defense).
            var extra = new byte[1];
            if (deflate.Read(extra, 0, 1) > 0)
                return null;
            return output;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static byte[] ReadSharedFileBytes(string path, int expectedLength)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        var buffer = new byte[expectedLength];
        if (!ReadExact(stream, buffer, 0, expectedLength))
            throw new IOException("Unexpected end of seed file.");
        return buffer;
    }

    private static byte[] ReadSharedFileSlice(string path, int offset, int length)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[length];
        if (!ReadExact(stream, buffer, 0, length))
            throw new IOException("Unexpected end of seed file slice.");
        return buffer;
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
                return false;
            total += read;
        }
        return true;
    }

    private string CreateDownloadDirectory(string version) =>
        Path.Combine(
            _updatesDirectory,
            DownloadPrefix + version + "-" + Guid.NewGuid().ToString("N")
        );

    private void PublishDownload(string download, string version)
    {
        var target = Path.Combine(_updatesDirectory, version);
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.Move(download, target);
        Volatile.Write(ref _verifiedVersion, version);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
        return ToHex(sha.ComputeHash(bytes));
    }

    private static string Sha256Hex(SHA256 sha, byte[] bytes, int offset, int count) =>
        ToHex(sha.ComputeHash(bytes, offset, count));

    private static string ToHex(byte[] hash)
    {
        var chars = new char[hash.Length * 2];
        const string digits = "0123456789abcdef";
        for (var i = 0; i < hash.Length; i++)
        {
            chars[i * 2] = digits[hash[i] >> 4];
            chars[(i * 2) + 1] = digits[hash[i] & 0xf];
        }
        return new string(chars);
    }

    private readonly record struct LocalChunkSlice(string FilePath, int Offset, int Length);

    private readonly record struct PackSlice(long Offset, int Length);

    private readonly record struct FetchBytesResult(
        bool Succeeded,
        byte[]? Bytes,
        string ReasonCode,
        bool Retryable = false
    )
    {
        public static FetchBytesResult Ok(byte[] bytes) => new(true, bytes, string.Empty);

        public static FetchBytesResult Failed(string reasonCode, bool retryable = false) =>
            new(false, null, reasonCode, retryable);
    }
}
