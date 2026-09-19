using System.Security.Cryptography;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterPreviewIdentityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"bpp-encounter-identity-{Guid.NewGuid():N}"
    );

    [Fact]
    public void Uses_GameData_etag_without_hashing_the_database()
    {
        Directory.CreateDirectory(_directory);
        var manifestPath = Path.Combine(_directory, "manifest.json");
        var databasePath = Path.Combine(_directory, "GameData.db");
        File.WriteAllText(manifestPath, """{"Entries":{"GameData":{"ETag":"\"etag-a\""}}}""");

        var identity = EncounterPreviewIdentityResolver.Resolve(
            manifestPath,
            databasePath,
            "https://data.example.invalid/",
            "build-a",
            "Online"
        );

        Assert.Equal("etag", identity.Kind);
        Assert.Equal("\"etag-a\"", identity.Value);
        Assert.Equal("https://data.example.invalid/GameData.db.zip", identity.Resource);
    }

    [Fact]
    public void Missing_or_corrupt_manifest_falls_back_to_database_sha256()
    {
        Directory.CreateDirectory(_directory);
        var manifestPath = Path.Combine(_directory, "manifest.json");
        var databasePath = Path.Combine(_directory, "GameData.db");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(databasePath, bytes);
        File.WriteAllText(manifestPath, "{broken");

        var identity = EncounterPreviewIdentityResolver.Resolve(
            manifestPath,
            databasePath,
            "https://data.example.invalid",
            "build-a",
            "Online"
        );

        Assert.Equal("sha256", identity.Kind);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            identity.Value
        );
    }

    [Fact]
    public void No_etag_and_no_database_cannot_create_a_stable_identity()
    {
        Directory.CreateDirectory(_directory);

        Assert.Throws<InvalidOperationException>(() =>
            EncounterPreviewIdentityResolver.Resolve(
                Path.Combine(_directory, "manifest.json"),
                Path.Combine(_directory, "GameData.db"),
                "https://data.example.invalid",
                "build-a",
                "Online"
            )
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
