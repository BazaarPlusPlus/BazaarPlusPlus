#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class BoundedTextTail
{
    internal const int DefaultCapacity = 4096;
    private const int ReadChunkSize = 256;

    private readonly char[] _buffer;
    private readonly object _sync = new();
    private int _start;
    private int _length;
    private bool _wasTruncated;

    internal BoundedTextTail(int capacity = DefaultCapacity)
    {
        if (capacity <= 0 || capacity > DefaultCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new char[capacity];
    }

    internal int Capacity => _buffer.Length;

    internal int Length
    {
        get
        {
            lock (_sync)
                return _length;
        }
    }

    internal bool WasTruncated
    {
        get
        {
            lock (_sync)
                return _wasTruncated;
        }
    }

    internal string Value
    {
        get
        {
            lock (_sync)
            {
                if (_length == 0)
                    return string.Empty;

                var value = new char[_length];
                var first = Math.Min(_length, _buffer.Length - _start);
                Array.Copy(_buffer, _start, value, 0, first);
                if (first < _length)
                    Array.Copy(_buffer, 0, value, first, _length - first);
                return new string(value);
            }
        }
    }

    internal void ReadFrom(TextReader reader)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var chunk = new char[ReadChunkSize];
        int read;
        while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
            Append(chunk, read);
    }

    internal void Append(char[] source, int count)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (count < 0 || count > source.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (_sync)
        {
            for (var index = 0; index < count; index++)
            {
                if (_length < _buffer.Length)
                {
                    _buffer[(_start + _length) % _buffer.Length] = source[index];
                    _length++;
                    continue;
                }

                _buffer[_start] = source[index];
                _start = (_start + 1) % _buffer.Length;
                _wasTruncated = true;
            }
        }
    }
}
