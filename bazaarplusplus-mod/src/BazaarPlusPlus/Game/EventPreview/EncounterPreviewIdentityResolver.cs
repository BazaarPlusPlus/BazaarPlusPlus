#nullable enable
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EncounterPreviewIdentityResolver
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
        if (TryReadGameDataEtag(manifestPath, out var etag))
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

    private static bool TryReadGameDataEtag(string manifestPath, out string etag)
    {
        etag = string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return false;

        try
        {
            var document = JObject.Parse(File.ReadAllText(manifestPath));
            etag = document.SelectToken("Entries.GameData.ETag")?.Value<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(etag);
        }
        catch (Exception ex)
            when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is Newtonsoft.Json.JsonException
            )
        {
            etag = string.Empty;
            return false;
        }
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
