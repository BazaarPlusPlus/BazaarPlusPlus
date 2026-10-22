#nullable enable
using System.Security.Cryptography;

namespace BazaarPlusPlus.ManagedPayload;

/// <summary>
/// A Managed Payload staged under the Data Root's <c>Updates/&lt;version&gt;/</c>: the signed
/// manifest, its signature, and the files under <see cref="ManagedPayloadManifest.FilesDirectory"/>.
/// Core's staging verifies it before publishing the directory, the Updater again before it swaps
/// anything in; both run this same check.
/// </summary>
internal static class StagedManagedPayload
{
    public const string UpdatesDirectoryName = "Updates";

    public static bool TryVerify(
        string directory,
        ManagedPayloadHost host,
        string platformKey,
        MinisignPublicKey publicKey,
        out ManagedPayloadManifest? manifest,
        out string failure
    )
    {
        manifest = null;
        var manifestPath = Path.Combine(directory, ManagedPayloadManifest.FileName);
        var signaturePath = Path.Combine(directory, ManagedPayloadManifest.SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
        {
            failure = "manifest_missing";
            return false;
        }

        var manifestBytes = File.ReadAllBytes(manifestPath);
        if (
            !Minisign.Verify(
                publicKey,
                manifestBytes,
                File.ReadAllText(signaturePath),
                out var signatureFailure
            )
        )
        {
            failure = signatureFailure;
            return false;
        }

        ManagedPayloadManifest parsed;
        try
        {
            parsed = ManagedPayloadManifest.Parse(manifestBytes);
        }
        catch (FormatException)
        {
            failure = "manifest_invalid";
            return false;
        }
        if (!string.Equals(parsed.Platform, platformKey, StringComparison.Ordinal))
        {
            failure = "platform_mismatch";
            return false;
        }

        foreach (var file in parsed.Files)
        {
            if (!host.MayWrite(file.Path) || file.Path == host.InstalledManifestPath)
            {
                failure = "path_outside_managed_payload";
                return false;
            }
            var staged = StagedFilePath(directory, file.Path);
            if (!File.Exists(staged) || new FileInfo(staged).Length != file.Size)
            {
                failure = "file_missing";
                return false;
            }
            if (!string.Equals(Sha256Hex(staged), file.Sha256, StringComparison.Ordinal))
            {
                failure = "file_hash_mismatch";
                return false;
            }
        }

        manifest = parsed;
        failure = string.Empty;
        return true;
    }

    public static string StagedFilePath(string directory, string installPath) =>
        Path.Combine(
            new[] { directory, ManagedPayloadManifest.FilesDirectory }
                .Concat(installPath.Split('/'))
                .ToArray()
        );

    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var chars = new char[hash.Length * 2];
        const string digits = "0123456789abcdef";
        for (var i = 0; i < hash.Length; i++)
        {
            chars[i * 2] = digits[hash[i] >> 4];
            chars[(i * 2) + 1] = digits[hash[i] & 0xf];
        }
        return new string(chars);
    }
}
