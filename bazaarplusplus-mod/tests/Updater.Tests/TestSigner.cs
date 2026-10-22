#nullable enable
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.ManagedPayload;

namespace Updater.Tests;

/// <summary>
/// RFC 8032 Ed25519 signing and the minisign box the Tauri signer writes, for test keys only.
/// Independent of the product's verifier, and checked against the RFC vectors, so the two
/// cross-check each other.
/// </summary>
internal sealed class TestSigner
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L =
        BigInteger.Pow(2, 252)
        + BigInteger.Parse(
            "27742317777372353535851937790883648493",
            System.Globalization.CultureInfo.InvariantCulture
        );
    private static readonly BigInteger D = Mod(-121665 * BigInteger.ModPow(121666, P - 2, P));
    private static readonly (BigInteger X, BigInteger Y) Base = (
        BigInteger.Parse(
            "15112221349535400772501151409588531511454012693041857206046113283949847762202",
            System.Globalization.CultureInfo.InvariantCulture
        ),
        BigInteger.Parse(
            "46316835694926478169428394003475163141307993866256225615783033603165251855960",
            System.Globalization.CultureInfo.InvariantCulture
        )
    );

    private readonly byte[] _seed;

    public TestSigner(byte[] seed, byte[] keyId)
    {
        _seed = seed;
        KeyId = keyId;
        var (scalar, _) = Expand(seed);
        PublicKey = Encode(Multiply(Base, scalar));
    }

    public byte[] KeyId { get; }
    public byte[] PublicKey { get; }

    public static TestSigner Default { get; } =
        new(
            Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(),
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        );

    public MinisignPublicKey MinisignKey =>
        MinisignPublicKey.Parse(
            Convert.ToBase64String(
                new byte[] { (byte)'E', (byte)'d' }
                    .Concat(KeyId)
                    .Concat(PublicKey)
                    .ToArray()
            )
        );

    public byte[] Sign(byte[] message)
    {
        var (scalar, prefix) = Expand(_seed);
        var r = Mod(FromLittleEndian(Sha512(prefix.Concat(message).ToArray())), L);
        var encodedR = Encode(Multiply(Base, r));
        var k = Mod(
            FromLittleEndian(Sha512(encodedR.Concat(PublicKey).Concat(message).ToArray())),
            L
        );
        var s = Mod(r + (k * scalar), L);
        return encodedR.Concat(ToLittleEndian(s, 32)).ToArray();
    }

    /// <summary>A minisign signature over <paramref name="data"/>, base64 encoded as Tauri stores it.</summary>
    public string SignMinisign(byte[] data, string trustedComment = "timestamp:0\tfile:test")
    {
        var signature = Sign(Blake2b.Hash512(data));
        var blob = new byte[] { (byte)'E', (byte)'D' }
            .Concat(KeyId)
            .Concat(signature)
            .ToArray();
        var global = Sign(signature.Concat(Encoding.UTF8.GetBytes(trustedComment)).ToArray());
        var box =
            "untrusted comment: signature from test key\n"
            + Convert.ToBase64String(blob)
            + "\ntrusted comment: "
            + trustedComment
            + "\n"
            + Convert.ToBase64String(global)
            + "\n";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(box));
    }

    private static (BigInteger Scalar, byte[] Prefix) Expand(byte[] seed)
    {
        var h = Sha512(seed);
        var a = h.Take(32).ToArray();
        a[0] &= 248;
        a[31] &= 127;
        a[31] |= 64;
        return (FromLittleEndian(a), h.Skip(32).ToArray());
    }

    private static (BigInteger X, BigInteger Y) Add(
        (BigInteger X, BigInteger Y) p,
        (BigInteger X, BigInteger Y) q
    )
    {
        var dxy = Mod(D * p.X * q.X * p.Y * q.Y);
        var x = Mod(((p.X * q.Y) + (q.X * p.Y)) * Inverse(1 + dxy));
        var y = Mod(((p.Y * q.Y) + (p.X * q.X)) * Inverse(1 - dxy));
        return (x, y);
    }

    private static (BigInteger X, BigInteger Y) Multiply(
        (BigInteger X, BigInteger Y) point,
        BigInteger scalar
    )
    {
        (BigInteger X, BigInteger Y) result = (0, 1);
        while (scalar > 0)
        {
            if (!scalar.IsEven)
                result = Add(result, point);
            point = Add(point, point);
            scalar >>= 1;
        }
        return result;
    }

    private static byte[] Encode((BigInteger X, BigInteger Y) point)
    {
        var bytes = ToLittleEndian(point.Y, 32);
        if (!point.X.IsEven)
            bytes[31] |= 0x80;
        return bytes;
    }

    private static byte[] ToLittleEndian(BigInteger value, int length)
    {
        var raw = value.ToByteArray();
        var bytes = new byte[length];
        Array.Copy(raw, bytes, Math.Min(length, raw.Length));
        return bytes;
    }

    private static BigInteger FromLittleEndian(byte[] bytes) =>
        new(bytes.Concat(new byte[] { 0 }).ToArray());

    private static byte[] Sha512(byte[] data) => SHA512.HashData(data);

    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

    private static BigInteger Mod(BigInteger value) => Mod(value, P);

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var result = value % modulus;
        return result.Sign < 0 ? result + modulus : result;
    }
}
