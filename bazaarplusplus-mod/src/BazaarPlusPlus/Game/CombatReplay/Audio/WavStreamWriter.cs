#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

/// <summary>
/// Streams 32-bit IEEE-float interleaved PCM into a canonical 44-byte WAV
/// (WAVE_FORMAT_IEEE_FLOAT = 3) file and back-patches the RIFF + data chunk
/// sizes on close. PCM stays float — the mux/AAC pass handles conversion.
/// Pure System.IO; no Unity/FMOD references. Writer-thread only.
/// </summary>
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

    public long DataByteLength => _dataByteLength;

    /// <summary>
    /// Appends <paramref name="floatCount"/> little-endian float samples starting
    /// at <paramref name="offset"/> within <paramref name="buffer"/>. Writer-thread only.
    /// </summary>
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

    /// <summary>
    /// Overrides the channel count written into the final WAV header on <see cref="Dispose"/>.
    /// The on-disk samples are channel-agnostic interleaved floats, so only the header fields
    /// need the corrected count.
    /// No-op for non-positive values. Owner only, before <see cref="Dispose"/>.
    /// </summary>
    public void SetChannelCount(int channels)
    {
        if (channels > 0)
            _channels = channels;
    }

    /// <summary>
    /// Seeks to the start, rewrites the header with the final RIFF + data sizes,
    /// flushes, and closes the file handle. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_closed)
            return;

        _closed = true;
        try
        {
            _stream.Seek(0, SeekOrigin.Begin);
            // Clamp to a valid 32-bit RIFF size. Combat replays never approach this, but
            // the guard keeps an over-long capture from back-patching a negative size.
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

    /// <summary>
    /// Builds the exact 44-byte canonical WAV header for 32-bit IEEE-float PCM.
    /// All multi-byte values are little-endian. This is the byte-exact unit-tested surface.
    /// </summary>
    internal static byte[] BuildHeader(int sampleRate, int channels, int dataByteLength)
    {
        var header = new byte[HeaderByteLength];

        // 0: "RIFF"
        WriteAscii(header, 0, "RIFF");
        // 4: uint32 LE riffChunkSize = 36 + dataByteLength
        WriteUInt32(header, 4, (uint)(36 + dataByteLength));
        // 8: "WAVE"
        WriteAscii(header, 8, "WAVE");
        // 12: "fmt "
        WriteAscii(header, 12, "fmt ");
        // 16: uint32 LE fmt chunk size = 16
        WriteUInt32(header, 16, 16);
        // 20: uint16 LE audioFormat = 3 (IEEE float)
        WriteUInt16(header, 20, 3);
        // 22: uint16 LE channels
        WriteUInt16(header, 22, (ushort)channels);
        // 24: uint32 LE sampleRate
        WriteUInt32(header, 24, (uint)sampleRate);
        // 28: uint32 LE byteRate = sampleRate * channels * 4
        WriteUInt32(header, 28, (uint)(sampleRate * channels * 4));
        // 32: uint16 LE blockAlign = channels * 4
        WriteUInt16(header, 32, (ushort)(channels * 4));
        // 34: uint16 LE bitsPerSample = 32
        WriteUInt16(header, 34, BitsPerSample);
        // 36: "data"
        WriteAscii(header, 36, "data");
        // 40: uint32 LE dataByteLength
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
