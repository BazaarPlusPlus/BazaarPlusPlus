#nullable enable
using System.Security.Cryptography;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.EventPreview;

public static class EncounterPreviewIdentityResolver
{
    public static EncounterPreviewCacheIdentity Resolve(
        string manifestPath,
        string databasePath,
        string dataBaseUrl,
        string gameBuild,
        string buildChannel
    )
    {
        var resource = $"{(dataBaseUrl ?? string.Empty).TrimEnd('/')}/GameData.db.zip";
        if (DataManifestEtag.TryRead(manifestPath, "GameData", out var etag))
        {
            return new EncounterPreviewCacheIdentity(
                "etag",
                resource,
                etag,
                gameBuild,
                buildChannel
            );
        }

        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            throw new InvalidOperationException(
                "GameData has neither a usable manifest ETag nor a readable database."
            );
        }

        return new EncounterPreviewCacheIdentity(
            "sha256",
            resource,
            ComputeSha256(databasePath),
            gameBuild,
            buildChannel
        );
    }

    private static string ComputeSha256(string databasePath)
    {
        using var stream = File.OpenRead(databasePath);
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(stream);
        var result = new char[bytes.Length * 2];
        const string Hex = "0123456789abcdef";
        for (var i = 0; i < bytes.Length; i++)
        {
            result[i * 2] = Hex[bytes[i] >> 4];
            result[i * 2 + 1] = Hex[bytes[i] & 0x0f];
        }
        return new string(result);
    }
}
