#nullable enable
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.TestSupport;
using BazaarPlusPlus.Updater;
using Xunit;

namespace Updater.Tests;

public sealed class ManifestTests
{
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void Parses_a_manifest()
    {
        var manifest = ManagedPayloadManifest.Parse(
            $"bpp-managed-payload-manifest 1\nversion 6.1.0\nplatform darwin-aarch64\nhistory-database-user-version 2\nnon-managed-payload-digest {Hash}\nfile {Hash} 12 BepInEx/plugins/A b.dll\nfile {Hash} 3 BepInEx/plugins/B.dll\n"
        );

        Assert.Equal("6.1.0", manifest.Version);
        Assert.Equal("darwin-aarch64", manifest.Platform);
        Assert.Equal(2, manifest.HistoryDatabaseUserVersion);
        Assert.Equal(Hash, manifest.NonManagedPayloadDigest);
        Assert.Equal(
            new[] { "BepInEx/plugins/A b.dll", "BepInEx/plugins/B.dll" },
            manifest.Files.Select(f => f.Path)
        );
        Assert.Equal(12, manifest.Files[0].Size);
    }

    [Fact]
    public void Reads_the_release_writers_golden()
    {
        var manifest = ManagedPayloadManifest.Parse(
            TestInputs.ScratchBytes(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "managed-payload-manifest.txt")
            )
        );

        Assert.Equal("6.1.0", manifest.Version);
        Assert.Equal("darwin-aarch64", manifest.Platform);
        Assert.Equal(1, manifest.HistoryDatabaseUserVersion);
        Assert.Equal(new string('e', 64), manifest.NonManagedPayloadDigest);
        Assert.Equal(
            new[]
            {
                "BepInEx/plugins/BazaarPlusPlus.Core.dll",
                "BepInEx/plugins/BazaarPlusPlus.dll",
            },
            manifest.Files.Select(f => f.Path)
        );
        Assert.Equal(4, manifest.Files[0].Size);
    }

    [Theory]
    [InlineData("version 6.1\n")]
    [InlineData("version 6.1.0.1\n")]
    [InlineData("file {h} 1 BepInEx/plugins/../x.dll\n")]
    [InlineData("file {h} 1 BepInEx\\plugins\\x.dll\n")]
    [InlineData("file {h} 1 BepInEx/plugins/B.dll\n")]
    [InlineData("file {h} -1 BepInEx/plugins/C.dll\n")]
    [InlineData("file ABC 1 BepInEx/plugins/C.dll\n")]
    public void Rejects_malformed_lines(string replacement)
    {
        var valid =
            $"bpp-managed-payload-manifest 1\nversion 6.1.0\nplatform darwin-aarch64\nhistory-database-user-version 1\nnon-managed-payload-digest {Hash}\nfile {Hash} 3 BepInEx/plugins/B.dll\n";
        var line = replacement.Replace("{h}", Hash, StringComparison.Ordinal);
        var text = line.StartsWith("version", StringComparison.Ordinal)
            ? valid.Replace("version 6.1.0\n", line, StringComparison.Ordinal)
            : valid + line;

        Assert.Throws<FormatException>(() => ManagedPayloadManifest.Parse(text));
    }

    [Fact]
    public void Rejects_crlf_and_a_missing_final_newline()
    {
        var text =
            $"bpp-managed-payload-manifest 1\nversion 6.1.0\nplatform darwin-aarch64\nhistory-database-user-version 1\nnon-managed-payload-digest {Hash}\nfile {Hash} 3 BepInEx/plugins/B.dll";
        Assert.Throws<FormatException>(() => ManagedPayloadManifest.Parse(text));
        Assert.Throws<FormatException>(() =>
            ManagedPayloadManifest.Parse(
                text.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n"
            )
        );
    }

    [Theory]
    [InlineData("BepInEx/plugins/BazaarPlusPlus.dll", true)]
    [InlineData("BepInEx/plugins/sub/BazaarPlusPlus.dll", false)]
    [InlineData("BepInEx/patchers/BazaarPlusPlus.Updater.dll", false)]
    [InlineData("BepInEx/core/BepInEx.dll", false)]
    [InlineData("winhttp.dll", false)]
    public void The_desktop_host_writes_only_directly_inside_plugins(string path, bool allowed)
    {
        Assert.Equal(allowed, ManagedPayloadHost.BepInEx.MayWrite(path));
    }

    [Theory]
    [InlineData("MelonLoader/Mods/BazaarPlusPlus.Core.dll", true)]
    [InlineData("MelonLoader/UserLibs/MessagePack.dll", true)]
    [InlineData("MelonLoader/Plugins/BazaarPlusPlus.Updater.dll", false)]
    [InlineData("lib/arm64-v8a/libe_sqlite3.so", false)]
    public void The_android_host_writes_only_mods_and_user_libs(string path, bool allowed)
    {
        Assert.Equal(allowed, ManagedPayloadHost.MelonLoader.MayWrite(path));
    }

    [Fact]
    public void The_updater_constants_match_their_owners()
    {
        Assert.Equal(PathConstants.DataRootDirectoryName, UpdaterRun.DataRootDirectoryName);
        Assert.Equal(
            BazaarPlusPlus.Infrastructure.ReleaseManifest.ReleaseManifestEndpoints.MacPlatformKey,
            UpdaterRun.MacPlatformKey
        );
        Assert.Equal(
            BazaarPlusPlus
                .Infrastructure
                .ReleaseManifest
                .ReleaseManifestEndpoints
                .WindowsPlatformKey,
            UpdaterRun.WindowsPlatformKey
        );
        Assert.Equal(
            BazaarPlusPlus
                .Infrastructure
                .ReleaseManifest
                .ReleaseManifestEndpoints
                .AndroidPlatformKey,
            UpdaterRun.AndroidPlatformKey
        );
    }
}
