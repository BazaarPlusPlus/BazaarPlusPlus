#nullable enable
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.TestSupport;
using BazaarPlusPlus.Updater;
using Updater.Tests;
using Xunit;

namespace SelfUpdate.Tests;

public sealed class IncrementalStagingTests : IDisposable
{
    private const string Platform = "darwin-aarch64";
    private const string Version = "6.0.1";
    private const string BaseUrl = "https://cdn.example/installer";
    private static readonly string Digest = new('d', 64);

    private readonly string _root = Directory.CreateTempSubdirectory("bpp-cdc-update-").FullName;

    private string InstallRoot => Path.Combine(_root, "game");
    private string DataRoot => Path.Combine(InstallRoot, "BazaarPlusPlusV6");
    private string Updates => SelfUpdatePaths.Updates(DataRoot);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Shared_FastCDC_golden_vectors_match_byte_for_byte()
    {
        var json = TestInputs.Scratch(
            Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "managed-payload",
                "cdc-vectors.json"
            )
        );
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            FastCdcParameters.AlgorithmName,
            doc.RootElement.GetProperty("algorithm").GetString()
        );

        foreach (var sample in doc.RootElement.GetProperty("gearSamples").EnumerateArray())
        {
            var index = sample.GetProperty("index").GetInt32();
            var expected = sample.GetProperty("value").GetUInt32();
            Assert.Equal(expected, FastCdcChunker.GearValue(index));
        }

        foreach (var testCase in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = testCase.GetProperty("name").GetString()!;
            var p = testCase.GetProperty("params");
            var parameters = new FastCdcParameters(
                p.GetProperty("minSize").GetInt32(),
                p.GetProperty("avgSize").GetInt32(),
                p.GetProperty("maxSize").GetInt32()
            );
            var seed = testCase.GetProperty("seed").GetUInt32();
            var length = testCase.GetProperty("length").GetInt32();
            var prefixHex = testCase.GetProperty("prefixHex").GetString()!;
            var prefix =
                prefixHex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(prefixHex);
            var body = PrngBytes(seed, length);
            var data = new byte[prefix.Length + body.Length];
            Buffer.BlockCopy(prefix, 0, data, 0, prefix.Length);
            Buffer.BlockCopy(body, 0, data, prefix.Length, body.Length);

            Assert.Equal(testCase.GetProperty("dataSha256").GetString(), Sha256Hex(data));

            var boundaries = FastCdcChunker.ChunkBoundaries(data, data.Length, parameters);
            var expectedBoundaries = testCase
                .GetProperty("boundaries")
                .EnumerateArray()
                .Select(e => e.GetInt32())
                .ToList();
            Assert.True(
                expectedBoundaries.SequenceEqual(boundaries),
                $"Boundaries mismatch for '{name}'."
            );

            var expectedHashes = testCase
                .GetProperty("chunkSha256s")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
            var actualHashes = new List<string>();
            var start = 0;
            foreach (var end in boundaries)
            {
                actualHashes.Add(Sha256Hex(data.AsSpan(start, end - start).ToArray()));
                start = end;
            }
            Assert.True(
                expectedHashes.SequenceEqual(actualHashes),
                $"Chunk hashes mismatch for '{name}'."
            );
        }
    }

    [Fact]
    public void Shared_chunk_index_golden_parses_and_validates_files()
    {
        var bytes = TestInputs.ScratchBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "managed-payload", "chunks.txt")
        );
        var parsed = ManagedPayloadChunkIndex.Parse(bytes);
        Assert.Equal("6.1.0", parsed.Version);
        Assert.Equal("darwin-aarch64", parsed.Platform);
        Assert.Equal(new FastCdcParameters(512, 2048, 8192), parsed.Chunking);
        Assert.Equal(3, parsed.Files.Count);
        Assert.Equal("BepInEx/plugins/Empty.txt", parsed.Files[1].Path);
        Assert.Empty(parsed.Files[1].Chunks);
        Assert.Equal("BepInEx/plugins/Sub File.dll", parsed.Files[2].Path);
    }

    [Fact]
    public async Task Incremental_cdc_stages_only_changed_chunks_and_the_updater_swaps_it_in()
    {
        var sharedA = PrngBytes(11, 48 * 1024);
        var sharedB = PrngBytes(22, 32 * 1024);
        var modOld = PrngBytes(33, 40 * 1024);
        // Modify only the final ~4 KiB of Mod.dll; keep sharedA, sharedB, and most of Mod.dll identical.
        var modNew = (byte[])modOld.Clone();
        var patch = PrngBytes(99, 4 * 1024);
        Buffer.BlockCopy(patch, 0, modNew, modNew.Length - patch.Length, patch.Length);

        Install(
            "6.0.0",
            ("SharedA.dll", sharedA),
            ("SharedB.dll", sharedB),
            ("Mod.dll", modOld),
            ("Removed.dll", Encoding.UTF8.GetBytes("old-removed"))
        );

        var bundle = BuildReleaseBundle(
            Version,
            TestSigner.Default,
            new FastCdcParameters(512, 2048, 8192),
            ("SharedA.dll", sharedA),
            ("SharedB.dll", sharedB),
            ("Mod.dll", modNew),
            ("Empty.txt", Array.Empty<byte>())
        );

        var server = new MockCdnHandler(bundle);
        var stager = Stager(seedInstallRoot: InstallRoot);
        var result = await stager.StageAsync(new HttpClient(server), bundle.Release, default);

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal("incremental", result.Mode);
        Assert.Null(result.IncrementalFallbackReason);
        Assert.True(result.DownloadedBytes < bundle.ZipBytes.Length);
        Assert.DoesNotContain(bundle.Release.Url.AbsoluteUri, server.RequestedUrls);
        Assert.Contains(bundle.Release.ChunkIndex!.Url.AbsoluteUri, server.RequestedUrls);
        // Only the small number of modified chunks were requested, far fewer than total chunks.
        var chunkRequests = server.RequestedUrls.Count(u =>
            u.EndsWith(".deflate", StringComparison.Ordinal)
        );
        Assert.True(
            chunkRequests > 0 && chunkRequests < bundle.Chunks.Count / 2,
            $"Expected partial chunk downloads, got {chunkRequests} of {bundle.Chunks.Count}."
        );

        // No temporary chunk pack left inside Updates/<version>/.
        var stagedDir = Path.Combine(Updates, Version);
        Assert.False(File.Exists(Path.Combine(stagedDir, ".chunks.tmp")));

        // Run the real Updater swap and verify exact bytes on disk.
        var log = new List<string>();
        var outcome = new ManagedPayloadSwap(
            InstallRoot,
            DataRoot,
            ManagedPayloadHost.BepInEx,
            Platform,
            TestSigner.Default.MinisignKey,
            new UpdaterLog(log.Add, log.Add, log.Add)
        ).Run();

        Assert.Equal(SwapOutcome.Applied, outcome);
        Assert.Equal(sharedA, TestInputs.ScratchBytes(InstallPath("BepInEx/plugins/SharedA.dll")));
        Assert.Equal(sharedB, TestInputs.ScratchBytes(InstallPath("BepInEx/plugins/SharedB.dll")));
        Assert.Equal(modNew, TestInputs.ScratchBytes(InstallPath("BepInEx/plugins/Mod.dll")));
        Assert.Empty(TestInputs.ScratchBytes(InstallPath("BepInEx/plugins/Empty.txt")));
        Assert.False(File.Exists(InstallPath("BepInEx/plugins/Removed.dll")));
    }

    [Fact]
    public async Task Incremental_falls_back_to_full_zip_when_no_local_seeds_match()
    {
        // Fresh directory with no matching files -> threshold exceeded -> automatic full-zip fallback.
        Directory.CreateDirectory(InstallPath("BepInEx/plugins"));
        var bundle = BuildReleaseBundle(
            Version,
            TestSigner.Default,
            new FastCdcParameters(512, 2048, 8192),
            ("A.dll", PrngBytes(7, 24 * 1024))
        );

        var server = new MockCdnHandler(bundle);
        var result = await Stager(seedInstallRoot: InstallRoot)
            .StageAsync(new HttpClient(server), bundle.Release, default);

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal("full", result.Mode);
        Assert.Equal("threshold_exceeded", result.IncrementalFallbackReason);
        Assert.Contains(bundle.Release.Url.AbsoluteUri, server.RequestedUrls);
        Assert.Equal(
            0,
            server.RequestedUrls.Count(u => u.EndsWith(".deflate", StringComparison.Ordinal))
        );
    }

    [Theory]
    [InlineData("index_404", "chunk_index_http_failure_status")]
    [InlineData("index_bad_sig", "chunk_index_signature_invalid")]
    [InlineData("chunk_404", "chunk_http_failure_status")]
    [InlineData("chunk_corrupt", "chunk_sha256_mismatch")]
    public async Task Incremental_failures_clean_up_and_fall_back_to_full_zip(
        string fault,
        string expectedFallback
    )
    {
        var shared = PrngBytes(11, 32 * 1024);
        var modOld = PrngBytes(33, 16 * 1024);
        var modNew = (byte[])modOld.Clone();
        modNew[modNew.Length - 1] ^= 0xff;

        Install("6.0.0", ("Shared.dll", shared), ("Mod.dll", modOld));
        var bundle = BuildReleaseBundle(
            Version,
            TestSigner.Default,
            new FastCdcParameters(512, 2048, 8192),
            ("Shared.dll", shared),
            ("Mod.dll", modNew)
        );
        var release = bundle.Release;
        var server = new MockCdnHandler(bundle);

        if (fault == "index_404")
            server.FaultIndexStatus = HttpStatusCode.NotFound;
        else if (fault == "index_bad_sig")
            release = release with
            {
                ChunkIndex = release.ChunkIndex! with
                {
                    Signature = TestSigner.Default.SignMinisign(new byte[] { 1 }),
                },
            };
        else if (fault == "chunk_404")
            server.FaultChunkStatus = HttpStatusCode.NotFound;
        else if (fault == "chunk_corrupt")
            server.CorruptChunkBytes = true;

        var result = await Stager(seedInstallRoot: InstallRoot)
            .StageAsync(new HttpClient(server), release, default);

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal("full", result.Mode);
        Assert.Equal(expectedFallback, result.IncrementalFallbackReason);
        Assert.Contains(release.Url.AbsoluteUri, server.RequestedUrls);
        Assert.Equal(new[] { Version }, Directory.GetDirectories(Updates).Select(Path.GetFileName));
    }

    private ManagedPayloadStager Stager(string? seedInstallRoot = null) =>
        new(
            Updates,
            Platform,
            SelfUpdateHost.Desktop,
            TestSigner.Default.MinisignKey,
            seedInstallRoot
        );

    private void Install(string version, params (string Name, byte[] Content)[] files)
    {
        foreach (var (name, content) in files)
            WriteBytes("BepInEx/plugins/" + name, content);
        WriteBytes(
            ManagedPayloadHost.BepInEx.InstalledManifestPath,
            Encoding.UTF8.GetBytes(ManifestText(version, files))
        );
    }

    private string InstallPath(string relative) =>
        Path.Combine(new[] { InstallRoot }.Concat(relative.Split('/')).ToArray());

    private void WriteBytes(string relative, byte[] content)
    {
        var path = InstallPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static ReleaseBundle BuildReleaseBundle(
        string version,
        TestSigner signer,
        FastCdcParameters chunking,
        params (string Name, byte[] Content)[] files
    )
    {
        var ordered = files
            .OrderBy(f => "BepInEx/plugins/" + f.Name, StringComparer.Ordinal)
            .ToArray();
        var manifestText = ManifestText(version, ordered);
        var manifestBytes = Encoding.UTF8.GetBytes(manifestText);
        var manifestSig = signer.SignMinisign(manifestBytes) + "\n";
        var manifestSigBytes = Encoding.UTF8.GetBytes(manifestSig);

        byte[] zipBytes;
        using (var zipBuffer = new MemoryStream())
        {
            using (var archive = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(archive, ManagedPayloadManifest.FileName, manifestBytes);
                Add(archive, ManagedPayloadManifest.SignatureFileName, manifestSigBytes);
                foreach (var (name, content) in ordered)
                    Add(archive, "files/BepInEx/plugins/" + name, content);
            }
            zipBytes = zipBuffer.ToArray();
        }

        var chunkRecords = new List<ManagedPayloadChunkRecord>();
        var chunkIndexByRawSha = new Dictionary<string, int>(StringComparer.Ordinal);
        var compressedBySha = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var fileLines = new List<string>();

        foreach (var (name, content) in ordered)
        {
            var boundaries = FastCdcChunker.ChunkBoundaries(content, content.Length, chunking);
            var refs = new List<int>();
            var start = 0;
            foreach (var end in boundaries)
            {
                var raw = content.AsSpan(start, end - start).ToArray();
                var rawSha = Sha256Hex(raw);
                if (!chunkIndexByRawSha.TryGetValue(rawSha, out var idx))
                {
                    var compressed = DeflateRaw(raw);
                    var compSha = Sha256Hex(compressed);
                    idx = chunkRecords.Count;
                    chunkIndexByRawSha[rawSha] = idx;
                    chunkRecords.Add(
                        new ManagedPayloadChunkRecord(
                            rawSha,
                            raw.Length,
                            compSha,
                            compressed.Length
                        )
                    );
                    compressedBySha[compSha] = compressed;
                }
                refs.Add(idx);
                start = end;
            }
            var refsText = refs.Count == 0 ? "-" : string.Join(",", refs);
            fileLines.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "file {0} {1} {2} BepInEx/plugins/{3}",
                    Sha256Hex(content),
                    content.Length,
                    refsText,
                    name
                )
            );
        }

        var sb = new StringBuilder()
            .Append(ManagedPayloadChunkIndex.Header)
            .Append('\n')
            .Append("version ")
            .Append(version)
            .Append('\n')
            .Append("platform ")
            .Append(Platform)
            .Append('\n')
            .AppendFormat(
                CultureInfo.InvariantCulture,
                "chunking {0} {1} {2} {3}\n",
                FastCdcParameters.AlgorithmName,
                chunking.MinSize,
                chunking.AvgSize,
                chunking.MaxSize
            )
            .Append("compression ")
            .Append(ManagedPayloadChunkIndex.CompressionName)
            .Append('\n')
            .Append("manifest-base64 ")
            .Append(Convert.ToBase64String(manifestBytes))
            .Append('\n')
            .Append("manifest-sig-base64 ")
            .Append(Convert.ToBase64String(manifestSigBytes))
            .Append('\n');
        foreach (var record in chunkRecords)
        {
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "chunk {0} {1} {2} {3}\n",
                record.RawSha256,
                record.RawSize,
                record.CompressedSha256,
                record.CompressedSize
            );
        }
        foreach (var line in fileLines)
            sb.Append(line).Append('\n');

        var indexBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var zipUrl = new Uri($"{BaseUrl}/{version}/{Platform}/managed/managed.zip");
        var indexUrl = new Uri($"{BaseUrl}/{version}/{Platform}/managed/managed.chunks.txt");
        var chunksBaseUrl = new Uri($"{BaseUrl}/managed/chunks");
        var release = new ManagedPayloadRelease(
            version,
            zipUrl,
            zipBytes.Length,
            Sha256Hex(zipBytes),
            signer.SignMinisign(zipBytes),
            1,
            Digest,
            new ManagedPayloadChunkIndexReference(
                indexUrl,
                indexBytes.Length,
                Sha256Hex(indexBytes),
                signer.SignMinisign(indexBytes),
                chunksBaseUrl
            )
        );
        return new ReleaseBundle(release, zipBytes, indexBytes, compressedBySha);
    }

    private static string ManifestText(string version, (string Name, byte[] Content)[] files)
    {
        var sb = new StringBuilder()
            .Append("bpp-managed-payload-manifest 1\n")
            .Append("version ")
            .Append(version)
            .Append('\n')
            .Append("platform ")
            .Append(Platform)
            .Append('\n')
            .Append("history-database-user-version 1\n")
            .Append("non-managed-payload-digest ")
            .Append(Digest)
            .Append('\n');
        foreach (
            var (name, content) in files.OrderBy(
                f => "BepInEx/plugins/" + f.Name,
                StringComparer.Ordinal
            )
        )
        {
            sb.Append("file ")
                .Append(Sha256Hex(content))
                .Append(' ')
                .Append(content.Length.ToString(CultureInfo.InvariantCulture))
                .Append(" BepInEx/plugins/")
                .Append(name)
                .Append('\n');
        }
        return sb.ToString();
    }

    private static void Add(ZipArchive archive, string name, byte[] bytes)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(bytes);
    }

    private static byte[] DeflateRaw(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    private static byte[] PrngBytes(uint seed, int length)
    {
        var outBytes = new byte[length];
        var state = seed;
        for (var i = 0; i < length; i++)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            outBytes[i] = (byte)(state >> 24);
        }
        return outBytes;
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record ReleaseBundle(
        ManagedPayloadRelease Release,
        byte[] ZipBytes,
        byte[] IndexBytes,
        Dictionary<string, byte[]> Chunks
    );

    private sealed class MockCdnHandler : HttpMessageHandler
    {
        private readonly ReleaseBundle _bundle;
        public ConcurrentBag<string> RequestedUrls { get; } = new();
        public HttpStatusCode? FaultIndexStatus { get; set; }
        public HttpStatusCode? FaultChunkStatus { get; set; }
        public bool CorruptChunkBytes { get; set; }

        public MockCdnHandler(ReleaseBundle bundle)
        {
            _bundle = bundle;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var uri = request.RequestUri!.AbsoluteUri;
            RequestedUrls.Add(uri);
            if (uri == _bundle.Release.Url.AbsoluteUri)
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_bundle.ZipBytes),
                    }
                );
            }
            if (uri == _bundle.Release.ChunkIndex!.Url.AbsoluteUri)
            {
                if (FaultIndexStatus is { } status)
                    return Task.FromResult(new HttpResponseMessage(status));
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_bundle.IndexBytes),
                    }
                );
            }
            if (uri.EndsWith(".deflate", StringComparison.Ordinal))
            {
                if (FaultChunkStatus is { } status)
                    return Task.FromResult(new HttpResponseMessage(status));
                var fileName = uri.Substring(uri.LastIndexOf('/') + 1);
                var sha = fileName.Substring(0, fileName.Length - ".deflate".Length);
                if (_bundle.Chunks.TryGetValue(sha, out var bytes))
                {
                    var payload = (byte[])bytes.Clone();
                    if (CorruptChunkBytes && payload.Length > 0)
                        payload[0] ^= 0xff;
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(payload),
                        }
                    );
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
