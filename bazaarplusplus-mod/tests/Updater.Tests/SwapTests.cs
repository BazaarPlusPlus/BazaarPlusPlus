#nullable enable
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.TestSupport;
using BazaarPlusPlus.Updater;
using Xunit;

namespace Updater.Tests;

public sealed class SwapTests : IDisposable
{
    private const string Platform = "darwin-aarch64";
    private const string Plugins = "BepInEx/plugins/";
    private static readonly string Digest = new('d', 64);

    private readonly string _root = Directory.CreateTempSubdirectory("bpp-updater-").FullName;
    private string _stageDigest = Digest;
    private readonly List<string> _log = new();

    private string InstallRoot => Path.Combine(_root, "game");
    private string Updates =>
        Path.Combine(InstallRoot, UpdaterRun.DataRootDirectoryName, "Updates");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Swaps_in_a_newer_payload_and_removes_only_managed_residue()
    {
        Install("6.0.0", ("A.dll", "a-old"), ("B.dll", "b-old"));
        WriteInstall(Plugins + "ThirdParty.dll", "theirs");
        Stage("6.0.1", ("A.dll", "a-new"), ("C.dll", "c-new"));

        Assert.Equal(SwapOutcome.Applied, Run());

        Assert.Equal("a-new", ReadInstall(Plugins + "A.dll"));
        Assert.Equal("c-new", ReadInstall(Plugins + "C.dll"));
        Assert.False(File.Exists(InstallPath(Plugins + "B.dll")));
        Assert.Equal("theirs", ReadInstall(Plugins + "ThirdParty.dll"));
        Assert.Equal(
            ManifestText("6.0.1", Platform, ("A.dll", "a-new"), ("C.dll", "c-new")),
            ReadInstall(ManagedPayloadHost.BepInEx.InstalledManifestPath)
        );
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
        Assert.Contains(
            _log,
            line =>
                line.Contains(
                    "event=updater.swap.applied from=6.0.0 to=6.0.1 files=2 removed=1",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void Swaps_over_a_development_install_by_its_version_file()
    {
        WriteInstall(Plugins + "BazaarPlusPlus.version", "6.0.0.t20261022.120000.dev\n");
        WriteInstall(Plugins + "A.dll", "a-dev");
        Stage("6.0.1", ("A.dll", "a-new"));

        Assert.Equal(SwapOutcome.Applied, Run());

        Assert.Equal("a-new", ReadInstall(Plugins + "A.dll"));
    }

    [Fact]
    public void Deletes_a_staged_payload_that_is_not_newer()
    {
        Install("6.0.1", ("A.dll", "a-installed"));
        Stage("6.0.1", ("A.dll", "a-same"));
        Stage("6.0.0", ("A.dll", "a-older"));

        Assert.Equal(SwapOutcome.NothingStaged, Run());

        Assert.Equal("a-installed", ReadInstall(Plugins + "A.dll"));
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public void Applies_the_newest_staged_payload_and_drops_the_older_ones()
    {
        Install("6.0.0", ("A.dll", "a-old"));
        Stage("6.0.1", ("A.dll", "a-601"));
        Stage("6.0.2", ("A.dll", "a-602"));

        Assert.Equal(SwapOutcome.Applied, Run());

        Assert.Equal("a-602", ReadInstall(Plugins + "A.dll"));
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public void Rejects_a_payload_signed_by_another_key()
    {
        Install("6.0.0", ("A.dll", "a-old"));
        Stage("6.0.1", new TestSigner(new byte[32], TestSigner.Default.KeyId), ("A.dll", "a-new"));

        Assert.Equal(SwapOutcome.Rejected, Run());

        Assert.Equal("a-old", ReadInstall(Plugins + "A.dll"));
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
        Assert.Contains(
            _log,
            line => line.Contains("reason_code=signature_invalid", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Rejects_a_file_changed_after_signing()
    {
        Install("6.0.0", ("A.dll", "a-old"));
        var staged = Stage("6.0.1", ("A.dll", "a-new"));
        File.WriteAllText(StagedManagedPayload.StagedFilePath(staged, Plugins + "A.dll"), "a-bad");

        Assert.Equal(SwapOutcome.Rejected, Run());

        Assert.Equal("a-old", ReadInstall(Plugins + "A.dll"));
        Assert.Contains(
            _log,
            line => line.Contains("reason_code=file_hash_mismatch", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Rejects_a_payload_for_another_platform_or_outside_the_plugins_directory()
    {
        Install("6.0.0", ("A.dll", "a-old"));
        Stage("6.0.1", TestSigner.Default, "windows-x86_64", Plugins, ("A.dll", "a-new"));

        Assert.Equal(SwapOutcome.Rejected, Run());
        Assert.True(
            _log.Any(line =>
                line.Contains("reason_code=platform_mismatch", StringComparison.Ordinal)
            ),
            string.Join("\n", _log)
        );

        Stage("6.0.2", TestSigner.Default, Platform, "BepInEx/patchers/", ("Evil.dll", "x"));
        Assert.Equal(SwapOutcome.Rejected, Run());
        Assert.Contains(
            _log,
            line =>
                line.Contains("reason_code=path_outside_managed_payload", StringComparison.Ordinal)
        );
        Assert.False(File.Exists(InstallPath("BepInEx/patchers/Evil.dll")));
        Assert.Equal("a-old", ReadInstall(Plugins + "A.dll"));
    }

    [Fact]
    public void Rejects_a_payload_whose_non_managed_files_differ_from_the_install()
    {
        Install("6.0.0", ("A.dll", "a-old"));
        _stageDigest = new string('f', 64);
        Stage("6.0.1", ("A.dll", "a-new"));

        Assert.Equal(SwapOutcome.Rejected, Run());

        Assert.Equal("a-old", ReadInstall(Plugins + "A.dll"));
        Assert.Contains(
            _log,
            line =>
                line.Contains("reason_code=non_managed_payload_mismatch", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_failed_step_rolls_every_file_back()
    {
        Install("6.0.0", ("A.dll", "a-old"), ("B.dll", "b-old"));
        Stage("6.0.1", ("A.dll", "a-new"), ("C.dll", "c-new"));
        // A directory where C.dll must go: moving the staged file in fails after A.dll and the
        // residue B.dll have moved.
        Directory.CreateDirectory(InstallPath(Plugins + "C.dll"));

        Assert.Equal(SwapOutcome.RolledBack, Run());

        Assert.Equal("a-old", ReadInstall(Plugins + "A.dll"));
        Assert.Equal("b-old", ReadInstall(Plugins + "B.dll"));
        Assert.Equal(
            ManifestText("6.0.0", Platform, ("A.dll", "a-old"), ("B.dll", "b-old")),
            ReadInstall(ManagedPayloadHost.BepInEx.InstalledManifestPath)
        );
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public void An_interrupted_swap_is_rolled_back_on_the_next_start()
    {
        Install("6.0.0", ("A.dll", "a-old"));
        var swap = Path.Combine(Updates, ".swap");
        Directory.CreateDirectory(Path.Combine(swap, "backup", "BepInEx", "plugins"));
        File.Move(
            InstallPath(Plugins + "A.dll"),
            Path.Combine(swap, "backup", "BepInEx", "plugins", "A.dll")
        );
        WriteInstall(Plugins + "A.dll", "a-half");
        File.WriteAllText(
            Path.Combine(swap, "journal.txt"),
            $"swap 6.0.1\naside {Plugins}A.dll\nplaced {Plugins}A.dll\n"
        );

        Assert.Equal(SwapOutcome.NothingStaged, Run());

        Assert.Equal("a-old", ReadInstall(Plugins + "A.dll"));
        Assert.False(Directory.Exists(swap));
        Assert.Contains(
            _log,
            line =>
                line.Contains(
                    "event=updater.swap.recovered version=6.0.1",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public void A_committed_swap_is_only_cleaned_up_on_the_next_start()
    {
        Install("6.0.1", ("A.dll", "a-new"));
        var swap = Path.Combine(Updates, ".swap");
        Directory.CreateDirectory(Path.Combine(swap, "backup", "BepInEx", "plugins"));
        File.WriteAllText(Path.Combine(swap, "backup", "BepInEx", "plugins", "A.dll"), "a-old");
        File.WriteAllText(
            Path.Combine(swap, "journal.txt"),
            $"swap 6.0.1\naside {Plugins}A.dll\nplaced {Plugins}A.dll\ncommit 6.0.1\n"
        );

        Run();

        Assert.Equal("a-new", ReadInstall(Plugins + "A.dll"));
        Assert.False(Directory.Exists(swap));
    }

    private SwapOutcome Run() =>
        new ManagedPayloadSwap(
            InstallRoot,
            Path.Combine(InstallRoot, UpdaterRun.DataRootDirectoryName),
            ManagedPayloadHost.BepInEx,
            Platform,
            TestSigner.Default.MinisignKey,
            new UpdaterLog(_log.Add, _log.Add, _log.Add)
        ).Run();

    private void Install(string version, params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
            WriteInstall(Plugins + name, content);
        WriteInstall(
            ManagedPayloadHost.BepInEx.InstalledManifestPath,
            ManifestText(version, Platform, files)
        );
    }

    private string Stage(string version, params (string Name, string Content)[] files) =>
        Stage(version, TestSigner.Default, Platform, Plugins, files);

    private string Stage(
        string version,
        TestSigner signer,
        params (string Name, string Content)[] files
    ) => Stage(version, signer, Platform, Plugins, files);

    private string Stage(
        string version,
        TestSigner signer,
        string platform,
        string directory,
        params (string Name, string Content)[] files
    )
    {
        var staged = Path.Combine(Updates, version);
        foreach (var (name, content) in files)
        {
            var path = StagedManagedPayload.StagedFilePath(staged, directory + name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        var manifest = Encoding.UTF8.GetBytes(
            ManifestText(version, platform, directory, files, _stageDigest)
        );
        File.WriteAllBytes(Path.Combine(staged, ManagedPayloadManifest.FileName), manifest);
        File.WriteAllText(
            Path.Combine(staged, ManagedPayloadManifest.SignatureFileName),
            signer.SignMinisign(manifest)
        );
        return staged;
    }

    private static string ManifestText(
        string version,
        string platform,
        params (string Name, string Content)[] files
    ) => ManifestText(version, platform, Plugins, files);

    private static string ManifestText(
        string version,
        string platform,
        string directory,
        (string Name, string Content)[] files,
        string? digest = null
    )
    {
        var builder = new StringBuilder()
            .Append("bpp-managed-payload-manifest 1\n")
            .Append("version ")
            .Append(version)
            .Append('\n')
            .Append("platform ")
            .Append(platform)
            .Append('\n')
            .Append("history-database-user-version 1\n")
            .Append("non-managed-payload-digest ")
            .Append(digest ?? Digest)
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

    private string InstallPath(string relative) =>
        Path.Combine(new[] { InstallRoot }.Concat(relative.Split('/')).ToArray());

    private void WriteInstall(string relative, string content)
    {
        var path = InstallPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string ReadInstall(string relative) => TestInputs.Scratch(InstallPath(relative));
}
