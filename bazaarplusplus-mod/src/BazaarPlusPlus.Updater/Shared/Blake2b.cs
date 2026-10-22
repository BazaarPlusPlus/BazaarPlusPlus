#nullable enable
namespace BazaarPlusPlus.ManagedPayload;

/// <summary>
/// BLAKE2b-512 (RFC 7693), unkeyed: the prehash a minisign "ED" signature covers. Self-contained
/// because the Updater may reference nothing it could replace, and neither host's base library
/// provides it.
/// </summary>
internal static class Blake2b
{
    private const int BlockBytes = 128;
    private const int OutBytes = 64;

    private static readonly ulong[] IV =
    {
        0x6a09e667f3bcc908UL,
        0xbb67ae8584caa73bUL,
        0x3c6ef372fe94f82bUL,
        0xa54ff53a5f1d36f1UL,
        0x510e527fade682d1UL,
        0x9b05688c2b3e6c1fUL,
        0x1f83d9abfb41bd6bUL,
        0x5be0cd19137e2179UL,
    };

    private static readonly byte[][] Sigma =
    {
        new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
        new byte[] { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
        new byte[] { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 },
        new byte[] { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
        new byte[] { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 },
        new byte[] { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
        new byte[] { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 },
        new byte[] { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
        new byte[] { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 },
        new byte[] { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 },
    };

    public static byte[] Hash512(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        using var stream = new MemoryStream(data, writable: false);
        return Hash512(stream);
    }

    public static byte[] Hash512(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var h = (ulong[])IV.Clone();
        // Parameter block: digest length 64, no key, fanout 1, depth 1.
        h[0] ^= 0x01010000UL ^ OutBytes;

        var block = new byte[BlockBytes];
        var next = new byte[BlockBytes];
        var filled = ReadFull(stream, block);
        ulong counter = 0;
        while (true)
        {
            var nextFilled = filled == BlockBytes ? ReadFull(stream, next) : 0;
            var last = nextFilled == 0;
            counter += (ulong)filled;
            if (last)
                Array.Clear(block, filled, BlockBytes - filled);
            Compress(h, block, counter, last);
            if (last)
                break;
            (block, next) = (next, block);
            filled = nextFilled;
        }

        var output = new byte[OutBytes];
        for (var i = 0; i < 8; i++)
        {
            for (var b = 0; b < 8; b++)
                output[(i * 8) + b] = (byte)(h[i] >> (8 * b));
        }
        return output;
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static void Compress(ulong[] h, byte[] block, ulong counter, bool last)
    {
        var m = new ulong[16];
        for (var i = 0; i < 16; i++)
        {
            ulong word = 0;
            for (var b = 7; b >= 0; b--)
                word = (word << 8) | block[(i * 8) + b];
            m[i] = word;
        }

        var v = new ulong[16];
        Array.Copy(h, v, 8);
        Array.Copy(IV, 0, v, 8, 8);
        v[12] ^= counter;
        if (last)
            v[14] = ~v[14];

        for (var round = 0; round < 12; round++)
        {
            var s = Sigma[round % 10];
            Mix(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            Mix(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            Mix(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            Mix(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            Mix(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            Mix(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            Mix(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            Mix(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }

        for (var i = 0; i < 8; i++)
            h[i] ^= v[i] ^ v[i + 8];
    }

    private static void Mix(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = RotateRight(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d];
        v[b] = RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = RotateRight(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d];
        v[b] = RotateRight(v[b] ^ v[c], 63);
    }

    private static ulong RotateRight(ulong value, int bits) =>
        (value >> bits) | (value << (64 - bits));
}
