#nullable enable
using System.Security.Cryptography;

namespace BazaarPlusPlus.ModApi.Bundle;

public sealed class UlidV5Generator
{
    private const long MaxTimestamp = 281_474_976_710_655;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private readonly Func<long> _clock;
    private readonly Action<byte[]> _fillRandom;
    private readonly object _sync = new();
    private readonly byte[] _lastRandom = new byte[10];
    private long _lastTimestamp = -1;

    public UlidV5Generator()
        : this(
            () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            bytes =>
            {
                using var random = RandomNumberGenerator.Create();
                random.GetBytes(bytes);
            }
        ) { }

    public UlidV5Generator(Func<long> clock, Action<byte[]> fillRandom)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _fillRandom = fillRandom ?? throw new ArgumentNullException(nameof(fillRandom));
    }

    public string Next()
    {
        lock (_sync)
        {
            var timestamp = _clock();
            if (timestamp < 0 || timestamp > MaxTimestamp)
                throw new ArgumentOutOfRangeException(nameof(timestamp));

            if (timestamp > _lastTimestamp)
            {
                _fillRandom(_lastRandom);
                _lastTimestamp = timestamp;
            }
            else
            {
                timestamp = _lastTimestamp;
                if (!Increment(_lastRandom))
                {
                    if (timestamp == MaxTimestamp)
                        throw new InvalidOperationException(
                            "ULID timestamp and randomness overflowed."
                        );
                    timestamp += 1;
                    Array.Clear(_lastRandom, 0, _lastRandom.Length);
                    _lastTimestamp = timestamp;
                }
            }

            return Encode(timestamp, _lastRandom);
        }
    }

    public static bool IsCanonical(string? value)
    {
        if (value == null || value.Length != 26 || value[0] > '7')
            return false;
        foreach (var character in value)
        {
            if (Alphabet.IndexOf(character) < 0)
                return false;
        }
        return true;
    }

    public static string Encode(long timestampMs, ReadOnlySpan<byte> randomness)
    {
        if (timestampMs < 0 || timestampMs > MaxTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestampMs));
        if (randomness.Length != 10)
            throw new ArgumentException(
                "ULID randomness must contain exactly 10 bytes.",
                nameof(randomness)
            );

        var output = new char[26];
        var timestamp = timestampMs;
        for (var index = 9; index >= 0; index--)
        {
            output[index] = Alphabet[(int)(timestamp & 31)];
            timestamp >>= 5;
        }

        for (var group = 0; group < 16; group++)
        {
            var value = 0;
            for (var bit = 0; bit < 5; bit++)
            {
                var bitIndex = group * 5 + bit;
                value = (value << 1) | ((randomness[bitIndex / 8] >> (7 - bitIndex % 8)) & 1);
            }
            output[10 + group] = Alphabet[value];
        }

        return new string(output);
    }

    private static bool Increment(byte[] value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            value[index]++;
            if (value[index] != 0)
                return true;
        }
        return false;
    }
}
