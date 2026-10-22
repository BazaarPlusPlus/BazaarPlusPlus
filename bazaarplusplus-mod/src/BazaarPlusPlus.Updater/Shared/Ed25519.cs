#nullable enable
using System.Numerics;
using System.Security.Cryptography;

namespace BazaarPlusPlus.ManagedPayload;

/// <summary>
/// Ed25519 signature verification (RFC 8032 §5.1.7), verify only. Field arithmetic uses
/// BigInteger, which both hosts' base libraries carry; a verification costs a few milliseconds,
/// and the Updater runs it once per start. It never handles a secret, so constant time does not
/// matter here.
/// </summary>
internal static class Ed25519
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L =
        BigInteger.Pow(2, 252)
        + BigInteger.Parse(
            "27742317777372353535851937790883648493",
            System.Globalization.CultureInfo.InvariantCulture
        );
    private static readonly BigInteger D = Mod(-121665 * Inverse(121666));
    private static readonly BigInteger D2 = Mod(2 * D);
    private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);
    private static readonly Point Base = BasePoint();

    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey == null || publicKey.Length != 32)
            return false;
        if (signature == null || signature.Length != 64 || message == null)
            return false;
        if (!TryDecode(publicKey, out var a))
            return false;

        var encodedR = new byte[32];
        Array.Copy(signature, 0, encodedR, 0, 32);
        if (!TryDecode(encodedR, out var r))
            return false;
        var s = FromLittleEndian(signature, 32, 32);
        if (s >= L)
            return false;

        byte[] digest;
        using (var sha = SHA512.Create())
        {
            var input = new byte[64 + message.Length];
            Array.Copy(encodedR, 0, input, 0, 32);
            Array.Copy(publicKey, 0, input, 32, 32);
            Array.Copy(message, 0, input, 64, message.Length);
            digest = sha.ComputeHash(input);
        }
        var h = Mod(FromLittleEndian(digest, 0, 64), L);

        var left = Multiply(Base, s);
        var right = Add(r, Multiply(a, h));
        return Equal(left, right);
    }

    private readonly struct Point
    {
        public Point(BigInteger x, BigInteger y, BigInteger z, BigInteger t)
        {
            X = x;
            Y = y;
            Z = z;
            T = t;
        }

        public BigInteger X { get; }
        public BigInteger Y { get; }
        public BigInteger Z { get; }
        public BigInteger T { get; }
    }

    private static Point BasePoint()
    {
        var y = Mod(4 * Inverse(5));
        var x = RecoverX(y, sign: 0) ?? throw new InvalidOperationException("Ed25519 base point");
        return new Point(x, y, BigInteger.One, Mod(x * y));
    }

    private static Point Identity() =>
        new(BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero);

    private static Point Add(Point p, Point q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(p.T * D2 * q.T);
        var d = Mod(2 * p.Z * q.Z);
        var e = b - a;
        var f = d - c;
        var g = d + c;
        var h = b + a;
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static Point Multiply(Point point, BigInteger scalar)
    {
        var result = Identity();
        var addend = point;
        while (scalar > 0)
        {
            if (!scalar.IsEven)
                result = Add(result, addend);
            addend = Add(addend, addend);
            scalar >>= 1;
        }
        return result;
    }

    private static bool Equal(Point p, Point q) =>
        Mod(p.X * q.Z) == Mod(q.X * p.Z) && Mod(p.Y * q.Z) == Mod(q.Y * p.Z);

    private static bool TryDecode(byte[] encoded, out Point point)
    {
        point = default;
        var bytes = (byte[])encoded.Clone();
        var sign = bytes[31] >> 7;
        bytes[31] &= 0x7f;
        var y = FromLittleEndian(bytes, 0, 32);
        if (y >= P)
            return false;
        var x = RecoverX(y, sign);
        if (x == null)
            return false;
        point = new Point(x.Value, y, BigInteger.One, Mod(x.Value * y));
        return true;
    }

    private static BigInteger? RecoverX(BigInteger y, int sign)
    {
        var y2 = Mod(y * y);
        var x2 = Mod((y2 - 1) * Inverse(Mod((D * y2) + 1)));
        if (x2.IsZero)
            return sign == 0 ? BigInteger.Zero : null;

        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (Mod((x * x) - x2) != 0)
            x = Mod(x * SqrtMinusOne);
        if (Mod((x * x) - x2) != 0)
            return null;
        if ((int)(x & 1) != sign)
            x = P - x;
        return x;
    }

    private static BigInteger FromLittleEndian(byte[] source, int offset, int count)
    {
        // A trailing zero byte keeps BigInteger's two's-complement reading non-negative.
        var bytes = new byte[count + 1];
        Array.Copy(source, offset, bytes, 0, count);
        return new BigInteger(bytes);
    }

    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(value, P - 2, P);

    private static BigInteger Mod(BigInteger value) => Mod(value, P);

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var result = value % modulus;
        return result.Sign < 0 ? result + modulus : result;
    }
}
