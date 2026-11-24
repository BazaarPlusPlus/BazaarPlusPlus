#nullable enable
using System.Security.Cryptography;

namespace BazaarPlusPlus.Infrastructure.SelfUpdate;

// Matches release/managed-payload-chunks.ts (fastcdc-gear32-v1), pinned by
// release/fixtures/managed-payload/cdc-vectors.json.
internal readonly record struct FastCdcParameters(int MinSize, int AvgSize, int MaxSize)
{
    public const string AlgorithmName = "fastcdc-gear32-v1";
    public const int WarmupBytes = 32;
    public const int MinBits = 10;
    public const int MaxBits = 18;
    public const int MaxAllowedChunkSize = 256 * 1024;

    public bool TryValidate(out int bits)
    {
        bits = 0;
        if (MinSize < WarmupBytes)
            return false;
        if (AvgSize <= 0 || (AvgSize & (AvgSize - 1)) != 0)
            return false;
        var b = 0;
        for (var v = AvgSize; v > 1; v >>= 1)
            b++;
        if (b < MinBits || b > MaxBits)
            return false;
        if (MinSize >= AvgSize || AvgSize >= MaxSize || MaxSize > MaxAllowedChunkSize)
            return false;
        bits = b;
        return true;
    }
}

internal static class FastCdcChunker
{
    private static readonly uint[] Gear = BuildGearTable();

    internal static uint GearValue(int index) => Gear[index];

    private static uint[] BuildGearTable()
    {
        var table = new uint[256];
        var input = new byte[1];
        using var sha = SHA256.Create();
        for (var i = 0; i < 256; i++)
        {
            input[0] = (byte)i;
            var digest = sha.ComputeHash(input);
            table[i] =
                (uint)digest[0]
                | ((uint)digest[1] << 8)
                | ((uint)digest[2] << 16)
                | ((uint)digest[3] << 24);
        }
        return table;
    }

    public static List<int> ChunkBoundaries(byte[] data, int length, FastCdcParameters parameters)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (length < 0 || length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (!parameters.TryValidate(out var bits))
            throw new ArgumentException("Invalid FastCDC parameters.", nameof(parameters));

        var boundaries = new List<int>();
        if (length == 0)
            return boundaries;

        var minSize = parameters.MinSize;
        var avgSize = parameters.AvgSize;
        var maxSize = parameters.MaxSize;
        var maskS = ~0u << (32 - (bits + 1));
        var maskL = ~0u << (32 - (bits - 1));
        var pos = 0;
        while (pos < length)
        {
            if (length - pos <= minSize)
            {
                boundaries.Add(length);
                break;
            }

            var end = Math.Min(pos + maxSize, length);
            var normal = Math.Min(pos + avgSize, end);
            uint fp = 0;
            var firstTest = pos + minSize - 1;
            for (var w = pos + minSize - FastCdcParameters.WarmupBytes; w < firstTest; w++)
                fp = (fp << 1) + Gear[data[w]];

            var cut = end;
            var i = firstTest;
            while (i < normal)
            {
                fp = (fp << 1) + Gear[data[i]];
                if ((fp & maskS) == 0u)
                {
                    cut = i + 1;
                    break;
                }
                i++;
            }
            if (cut == end)
            {
                while (i < end)
                {
                    fp = (fp << 1) + Gear[data[i]];
                    if ((fp & maskL) == 0u)
                    {
                        cut = i + 1;
                        break;
                    }
                    i++;
                }
            }

            boundaries.Add(cut);
            pos = cut;
        }

        return boundaries;
    }
}
