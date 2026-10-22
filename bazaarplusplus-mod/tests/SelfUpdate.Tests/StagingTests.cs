#nullable enable
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Infrastructure.SelfUpdate;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.TestSupport;
using BazaarPlusPlus.Updater;
using Updater.Tests;
using Xunit;

namespace SelfUpdate.Tests;

/// <summary>
/// Staging from a downloaded Managed Payload zip, and the Updater swapping in what staging
/// published. Failure modes: a zip that is not the published one (size, hash, signature), a
/// manifest signed by another key or naming another version, an entry outside the manifest or
/// outside the plugins directory, and a failed download leaving a staged version behind.
/// </summary>
public sealed class StagingTests : IDisposable
{
    private const string Platform = "darwin-aarch64";
    private const string Version = "6.0.1";
    private static readonly string Digest = new('d', 64);

    private readonly string _root = Directory.CreateTempSubdirectory("bpp-self-update-").FullName;

    private string InstallRoot => Path.Combine(_root, "game");
    private string DataRoot => Path.Combine(InstallRoot, "BazaarPlusPlusV6");
    private string Updates => SelfUpdatePaths.Updates(DataRoot);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_verified_download_is_staged_and_the_updater_swaps_it_in()
    {
        Install("6.0.0", ("A.dll", "a-old"), ("Gone.dll", "gone"));
        var zip = Zip(Version, TestSigner.Default, ("A.dll", "a-new"), ("B.dll", "b-new"));
        var stager = Stager();

        var result = await stager.StageAsync(Http(zip), Release(zip), CancellationToken.None);

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.True(stager.IsStaged(Version));
        Assert.Equal(new[] { Version }, Directory.GetDirectories(Updates).Select(Path.GetFileName));

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
        Assert.Equal("a-new", Read("BepInEx/plugins/A.dll"));
        Assert.Equal("b-new", Read("BepInEx/plugins/B.dll"));
        Assert.False(File.Exists(InstallPath("BepInEx/plugins/Gone.dll")));
        var running = RunningManagedPayload.Read(
            SelfUpdatePaths.InstalledManifest(InstallRoot, SelfUpdateHost.Desktop),
            Version,
            1
        );
        Assert.Equal(Digest, running.NonManagedPayloadDigest);
    }

    [Fact]
    public async Task A_staged_version_is_not_downloaded_again()
    {
        var zip = Zip(Version, TestSigner.Default, ("A.dll", "a-new"));
        var handler = new Handler(zip);
        var stager = Stager();
        Assert.True(
            (await stager.StageAsync(new HttpClient(handler), Release(zip), default)).Succeeded
        );
        Assert.True(
            (await stager.StageAsync(new HttpClient(handler), Release(zip), default)).Succeeded
        );
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task A_zip_that_is_not_the_published_one_is_refused()
    {
        var zip = Zip(Version, TestSigner.Default, ("A.dll", "a-new"));
        var stager = Stager();

        Assert.Equal(
            "size_mismatch",
            (
                await stager.StageAsync(
                    Http(zip),
                    Release(zip) with
                    {
                        Size = zip.Length + 1,
                    },
                    default
                )
            ).ReasonCode
        );
        Assert.Equal(
            "sha256_mismatch",
            (
                await stager.StageAsync(
                    Http(zip),
                    Release(zip) with
                    {
                        Sha256 = new string('0', 64),
                    },
                    default
                )
            ).ReasonCode
        );
        Assert.Equal(
            "zip_signature_invalid",
            (
                await stager.StageAsync(
                    Http(zip),
                    Release(zip) with
                    {
                        Signature = TestSigner.Default.SignMinisign(new byte[] { 1 }),
                    },
                    default
                )
            ).ReasonCode
        );
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public async Task A_manifest_signed_by_another_key_or_for_another_version_is_refused()
    {
        var stager = Stager();
        var otherKey = new TestSigner(new byte[32], TestSigner.Default.KeyId);
        // The zip is the published one; only its manifest's signature is foreign.
        var foreign = Zip(Version, otherKey, ("A.dll", "a"));
        Assert.Equal(
            "signature_invalid",
            (await stager.StageAsync(Http(foreign), Release(foreign), default)).ReasonCode
        );

        var older = Zip("6.0.0", TestSigner.Default, ("A.dll", "a"));
        Assert.Equal(
            "version_mismatch",
            (await stager.StageAsync(Http(older), Release(older), default)).ReasonCode
        );
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public async Task Only_manifest_named_plugin_files_leave_the_zip()
    {
        var stager = Stager();
        var escaping = Zip(
            Version,
            TestSigner.Default,
            "BepInEx/patchers/",
            new[] { ("X.dll", "x") },
            Array.Empty<(string, string)>()
        );
        Assert.Equal(
            "path_outside_managed_payload",
            (await stager.StageAsync(Http(escaping), Release(escaping), default)).ReasonCode
        );

        var extra = Zip(
            Version,
            TestSigner.Default,
            "BepInEx/plugins/",
            new[] { ("A.dll", "a") },
            new[] { ("../../escape.txt", "x"), ("files/BepInEx/plugins/Unlisted.dll", "u") }
        );
        Assert.True((await stager.StageAsync(Http(extra), Release(extra), default)).Succeeded);
        var staged = Path.Combine(Updates, Version);
        Assert.False(
            File.Exists(StagedManagedPayload.StagedFilePath(staged, "BepInEx/plugins/Unlisted.dll"))
        );
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public async Task A_body_without_a_length_is_read_no_further_than_the_published_size()
    {
        var zip = Zip(Version, TestSigner.Default, ("A.dll", "a-new"));
        var body = new Tripwire(zip.Length * 1000L, tripAt: zip.Length + 81920L * 2);
        var unsized = new HttpClient(new Handler(body));

        Assert.Equal(
            "size_mismatch",
            (await Stager().StageAsync(unsized, Release(zip), default)).ReasonCode
        );
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public async Task A_failed_download_leaves_nothing_staged()
    {
        var stager = Stager();
        var zip = Zip(Version, TestSigner.Default, ("A.dll", "a"));
        var failing = new HttpClient(new Handler(zip, HttpStatusCode.ServiceUnavailable));
        Assert.Equal(
            "http_failure_status",
            (await stager.StageAsync(failing, Release(zip), default)).ReasonCode
        );
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
        Assert.False(stager.IsStaged(Version));
    }

    [Fact]
    public async Task The_staging_run_is_shared_while_in_flight_and_retried_after_failure()
    {
        var gate = new TaskCompletionSource<ManagedPayloadStageResult>();
        var calls = 0;
        Task<ManagedPayloadStageResult> Stage()
        {
            calls++;
            return gate.Task;
        }
        var first = SelfUpdateStaging.Ensure("9.9.1", Stage);
        var second = SelfUpdateStaging.Ensure("9.9.1", Stage);
        gate.SetResult(ManagedPayloadStageResult.Failed("x"));
        await first;
        await second;
        Assert.Equal(1, calls);

        await SelfUpdateStaging.Ensure(
            "9.9.1",
            () => Task.FromResult(ManagedPayloadStageResult.Staged)
        );
        await SelfUpdateStaging.Ensure("9.9.1", Stage);
        Assert.Equal(1, calls);
    }

    private ManagedPayloadStager Stager() =>
        new(Updates, Platform, SelfUpdateHost.Desktop, TestSigner.Default.MinisignKey);

    private static ManagedPayloadRelease Release(byte[] zip) =>
        new(
            Version,
            new Uri("https://cdn.example/managed.zip"),
            zip.Length,
            Convert.ToHexStringLower(SHA256.HashData(zip)),
            TestSigner.Default.SignMinisign(zip),
            1,
            Digest
        );

    private static HttpClient Http(byte[] zip) => new(new Handler(zip));

    // A Managed Payload zip whose manifest `manifestSigner` signs; Release() signs the zip itself
    // with the trusted test key.
    private static byte[] Zip(
        string version,
        TestSigner manifestSigner,
        params (string Name, string Content)[] files
    ) => Zip(version, manifestSigner, "BepInEx/plugins/", files, Array.Empty<(string, string)>());

    private static byte[] Zip(
        string version,
        TestSigner manifestSigner,
        string directory,
        (string Name, string Content)[] files,
        (string Name, string Content)[] extraEntries
    )
    {
        var manifest = Encoding.UTF8.GetBytes(ManifestText(version, directory, files));
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, ManagedPayloadManifest.FileName, manifest);
            Add(
                archive,
                ManagedPayloadManifest.SignatureFileName,
                Encoding.UTF8.GetBytes(manifestSigner.SignMinisign(manifest))
            );
            foreach (var (name, content) in files)
                Add(archive, "files/" + directory + name, Encoding.UTF8.GetBytes(content));
            foreach (var (name, content) in extraEntries)
                Add(archive, name, Encoding.UTF8.GetBytes(content));
        }
        return buffer.ToArray();
    }

    private static void Add(ZipArchive archive, string name, byte[] bytes)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(bytes);
    }

    private static string ManifestText(
        string version,
        string directory,
        (string Name, string Content)[] files
    )
    {
        var builder = new StringBuilder()
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
                file => directory + file.Name,
                StringComparer.Ordinal
            )
        )
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            builder
                .Append("file ")
                .Append(Convert.ToHexStringLower(SHA256.HashData(bytes)))
                .Append(' ')
                .Append(bytes.Length)
                .Append(' ')
                .Append(directory)
                .Append(name)
                .Append('\n');
        }
        return builder.ToString();
    }

    private void Install(string version, params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
            Write("BepInEx/plugins/" + name, content);
        Write(
            ManagedPayloadHost.BepInEx.InstalledManifestPath,
            ManifestText(version, "BepInEx/plugins/", files)
        );
    }

    private string InstallPath(string relative) =>
        Path.Combine(new[] { InstallRoot }.Concat(relative.Split('/')).ToArray());

    private void Write(string relative, string content)
    {
        var path = InstallPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => TestInputs.Scratch(InstallPath(relative));

    // A body with no known length that fails the test once read past `tripAt` bytes.
    private sealed class Tripwire : Stream
    {
        private readonly long _length;
        private readonly long _tripAt;
        private long _position;

        public Tripwire(long length, long tripAt)
        {
            _length = length;
            _tripAt = tripAt;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position > _tripAt)
                throw new InvalidOperationException($"Read {_position} bytes past the size.");
            var read = (int)Math.Min(count, _length - _position);
            Array.Clear(buffer, offset, read);
            _position += read;
            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpContent> _body;
        private readonly HttpStatusCode _status;

        public Handler(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = () => new ByteArrayContent(body);
            _status = status;
        }

        public Handler(Stream body)
        {
            _body = () => new StreamContent(body);
            _status = HttpStatusCode.OK;
        }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = _body() });
        }
    }
}
