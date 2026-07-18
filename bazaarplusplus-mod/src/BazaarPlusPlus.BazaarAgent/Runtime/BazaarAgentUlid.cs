#nullable enable
using System.Security.Cryptography;

namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentUlid
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly object _gate = new();
    private static readonly byte[] _lastRand = new byte[10];
    private static long _lastTs;

    public static string New()
    {
        long ts;
        Span<byte> rand = stackalloc byte[10];
        lock (_gate)
        {
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ts > _lastTs)
            {
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(_lastRand);
                _lastTs = ts;
            }
            else
            {
                IncrementBigEndian(_lastRand);
                ts = _lastTs;
            }
            new ReadOnlySpan<byte>(_lastRand).CopyTo(rand);
        }

        Span<char> chars = stackalloc char[26];
        EncodeTimestamp(ts, chars.Slice(0, 10));
        EncodeRandomness(rand, chars.Slice(10));
        return new string(chars);
    }

    private static void IncrementBigEndian(byte[] r)
    {
        for (var i = r.Length - 1; i >= 0; i--)
        {
            if (++r[i] != 0)
                return;
        }
        // 80-bit overflow within one ms is astronomically unlikely; reseed defensively.
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(r);
    }

    private static void EncodeTimestamp(long ts, Span<char> dst)
    {
        for (var i = 9; i >= 0; i--)
        {
            dst[i] = Alphabet[(int)(ts & 0x1F)];
            ts >>= 5;
        }
    }

    private static void EncodeRandomness(ReadOnlySpan<byte> rand, Span<char> dst)
    {
        // 80 bits → 16 chars (5 bits each). Treat the 10 bytes as an 80-bit big-endian integer.
        ulong hi =
            ((ulong)rand[0] << 32)
            | ((ulong)rand[1] << 24)
            | ((ulong)rand[2] << 16)
            | ((ulong)rand[3] << 8)
            | rand[4];
        ulong lo =
            ((ulong)rand[5] << 32)
            | ((ulong)rand[6] << 24)
            | ((ulong)rand[7] << 16)
            | ((ulong)rand[8] << 8)
            | rand[9];
        for (var i = 7; i >= 0; i--)
        {
            dst[i] = Alphabet[(int)(hi & 0x1F)];
            hi >>= 5;
        }
        for (var i = 15; i >= 8; i--)
        {
            dst[i] = Alphabet[(int)(lo & 0x1F)];
            lo >>= 5;
        }
    }
}
