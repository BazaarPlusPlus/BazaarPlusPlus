#nullable enable
using System.Text;

namespace BazaarPlusPlus.ManagedPayload;

/// <summary>A minisign Ed25519 public key: its 8-byte key id and 32-byte key.</summary>
internal sealed class MinisignPublicKey
{
    private MinisignPublicKey(byte[] keyId, byte[] key)
    {
        KeyId = keyId;
        Key = key;
    }

    public byte[] KeyId { get; }
    public byte[] Key { get; }

    /// <summary>
    /// Reads the key line of a minisign public key: "Ed", the key id, and the key, base64 encoded.
    /// </summary>
    public static MinisignPublicKey Parse(string keyLine)
    {
        var bytes = Convert.FromBase64String((keyLine ?? string.Empty).Trim());
        if (bytes.Length != 42 || bytes[0] != (byte)'E' || bytes[1] != (byte)'d')
            throw new FormatException("Not a minisign Ed25519 public key.");
        var keyId = new byte[8];
        var key = new byte[32];
        Array.Copy(bytes, 2, keyId, 0, 8);
        Array.Copy(bytes, 10, key, 0, 32);
        return new MinisignPublicKey(keyId, key);
    }
}

/// <summary>
/// Verifies minisign signatures as the Tauri updater signer writes them: the signature box
/// itself or, as Tauri stores it, the box base64 encoded once more. Only the prehashed "ED"
/// algorithm the Tauri signer writes is accepted: the signature over the data's BLAKE2b-512
/// digest and the global signature over the trusted comment must both verify under the expected
/// key id.
/// </summary>
internal static class Minisign
{
    private const string UntrustedPrefix = "untrusted comment:";
    private const string TrustedPrefix = "trusted comment: ";

    public static bool Verify(
        MinisignPublicKey publicKey,
        byte[] data,
        string signature,
        out string failure
    )
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        return VerifyPrehashed(publicKey, Blake2b.Hash512(data), signature, out failure);
    }

    /// <summary>
    /// Verifies an "ED" signature given the BLAKE2b-512 digest of the data instead of the data.
    /// </summary>
    public static bool VerifyPrehashed(
        MinisignPublicKey publicKey,
        byte[] blake2b512,
        string signature,
        out string failure
    )
    {
        if (blake2b512 == null || blake2b512.Length != 64)
            throw new ArgumentException("A BLAKE2b-512 digest is 64 bytes.", nameof(blake2b512));
        if (publicKey == null)
            throw new ArgumentNullException(nameof(publicKey));

        if (!TryReadBox(signature, out var lines))
        {
            failure = "signature_unreadable";
            return false;
        }

        byte[] signatureBytes;
        byte[] globalSignature;
        try
        {
            signatureBytes = Convert.FromBase64String(lines[1].Trim());
            globalSignature = Convert.FromBase64String(lines[3].Trim());
        }
        catch (FormatException)
        {
            failure = "signature_unreadable";
            return false;
        }
        if (signatureBytes.Length != 74 || globalSignature.Length != 64)
        {
            failure = "signature_unreadable";
            return false;
        }

        if (signatureBytes[0] != (byte)'E' || signatureBytes[1] != (byte)'D')
        {
            failure = "signature_algorithm_unsupported";
            return false;
        }
        for (var i = 0; i < 8; i++)
        {
            if (signatureBytes[2 + i] != publicKey.KeyId[i])
            {
                failure = "signature_key_id_mismatch";
                return false;
            }
        }

        var edSignature = new byte[64];
        Array.Copy(signatureBytes, 10, edSignature, 0, 64);
        if (!Ed25519.Verify(publicKey.Key, blake2b512, edSignature))
        {
            failure = "signature_invalid";
            return false;
        }

        var trustedComment = Encoding.UTF8.GetBytes(lines[2].Substring(TrustedPrefix.Length));
        var globalMessage = new byte[64 + trustedComment.Length];
        Array.Copy(edSignature, 0, globalMessage, 0, 64);
        Array.Copy(trustedComment, 0, globalMessage, 64, trustedComment.Length);
        if (!Ed25519.Verify(publicKey.Key, globalMessage, globalSignature))
        {
            failure = "trusted_comment_signature_invalid";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool TryReadBox(string signature, out string[] lines)
    {
        lines = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        var text = signature.Trim();
        if (!text.StartsWith(UntrustedPrefix, StringComparison.Ordinal))
        {
            try
            {
                text = Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        var split = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (
            split.Length != 4
            || !split[0].StartsWith(UntrustedPrefix, StringComparison.Ordinal)
            || !split[2].StartsWith(TrustedPrefix, StringComparison.Ordinal)
        )
            return false;
        lines = split;
        return true;
    }
}
