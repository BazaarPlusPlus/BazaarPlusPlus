#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Audio;

// Use from the writer thread only.
internal sealed class WavStreamWriter : IDisposable
{
    private const int HeaderByteLength = 44;
    private const int BitsPerSample = 32;

    private readonly FileStream _stream;
    private readonly int _sampleRate;
    private int _channels;
    private long _dataByteLength;
    private byte[]? _scratch;
    private bool _closed;

    public WavStreamWriter(string filePath, int sampleRate, int channels)
    {
        FilePath = filePath;
        _sampleRate = sampleRate;
        _channels = channels;
        _stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

        var placeholder = BuildHeader(sampleRate, channels, dataByteLength: 0);
        _stream.Write(placeholder, 0, placeholder.Length);
    }

    public string FilePath { get; }

    public void WriteSamples(float[] buffer, int offset, int floatCount)
    {
        if (floatCount <= 0)
            return;

        int byteLen = floatCount * 4;
        if (_scratch == null || _scratch.Length < byteLen)
            _scratch = new byte[byteLen];

        Buffer.BlockCopy(buffer, offset * 4, _scratch, 0, byteLen);
        _stream.Write(_scratch, 0, byteLen);
        _dataByteLength += byteLen;
    }

    public void Dispose()
    {
        if (_closed)
            return;

        _closed = true;
        try
        {
            _stream.Seek(0, SeekOrigin.Begin);
            // The RIFF size includes header bytes and must fit in the 32-bit size field.
            var dataLen = (int)Math.Min(_dataByteLength, int.MaxValue - 36L);
            var header = BuildHeader(_sampleRate, _channels, dataLen);
            _stream.Write(header, 0, header.Length);
            _stream.Flush();
        }
        finally
        {
            _stream.Dispose();
        }
    }

    internal static byte[] BuildHeader(int sampleRate, int channels, int dataByteLength)
    {
        var header = new byte[HeaderByteLength];

        WriteAscii(header, 0, "RIFF");
        WriteUInt32(header, 4, (uint)(36 + dataByteLength));
        WriteAscii(header, 8, "WAVE");
        WriteAscii(header, 12, "fmt ");
        WriteUInt32(header, 16, 16);
        // 20: uint16 LE audioFormat = 3 (IEEE float)
        WriteUInt16(header, 20, 3);
        WriteUInt16(header, 22, (ushort)channels);
        WriteUInt32(header, 24, (uint)sampleRate);
        WriteUInt32(header, 28, (uint)(sampleRate * channels * 4));
        WriteUInt16(header, 32, (ushort)(channels * 4));
        WriteUInt16(header, 34, BitsPerSample);
        WriteAscii(header, 36, "data");
        WriteUInt32(header, 40, (uint)dataByteLength);

        return header;
    }

    private static void WriteAscii(byte[] target, int offset, string fourCc)
    {
        target[offset + 0] = (byte)fourCc[0];
        target[offset + 1] = (byte)fourCc[1];
        target[offset + 2] = (byte)fourCc[2];
        target[offset + 3] = (byte)fourCc[3];
    }

    private static void WriteUInt16(byte[] target, int offset, ushort value)
    {
        target[offset + 0] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset + 0] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
        target[offset + 2] = (byte)((value >> 16) & 0xFF);
        target[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
