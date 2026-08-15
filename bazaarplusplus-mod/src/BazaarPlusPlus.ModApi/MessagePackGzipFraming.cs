#nullable enable
using System.IO.Compression;
using MessagePack;

namespace BazaarPlusPlus.ModApi;

// This type shares only gzip framing, the bounded decompression loop, and failure classification.
// Each wrapper keeps its own size policy on purpose: V5 bundles arrive over the network and cap
// decompression at RunPayloadV5Codec.MaxDecompressedBytes, while locally recorded replays are
// written by this process and stay deliberately uncapped. Adding a cap to the local path would
// reject the mod's own large recordings.
internal enum MessagePackGzipFailureKind
{
    Empty,
    NotGzip,
    DecompressedTooLarge,
    DeserializedNull,
    DecodeFailed,
}

internal readonly struct MessagePackGzipDecodeResult<T>
    where T : class
{
    private MessagePackGzipDecodeResult(
        T? value,
        MessagePackGzipFailureKind? failureKind,
        Exception? exception
    )
    {
        Value = value;
        FailureKind = failureKind;
        Exception = exception;
    }

    public T? Value { get; }
    public MessagePackGzipFailureKind? FailureKind { get; }
    public Exception? Exception { get; }
    public bool Succeeded => Value != null;

    public static MessagePackGzipDecodeResult<T> Success(T value) => new(value, null, null);

    public static MessagePackGzipDecodeResult<T> Failure(
        MessagePackGzipFailureKind failureKind,
        Exception? exception = null
    ) => new(null, failureKind, exception);
}

internal static class MessagePackGzipFraming
{
    public static byte[] Encode<T>(T payload, MessagePackSerializerOptions options)
        where T : class
    {
        var packed = MessagePackSerializer.Serialize(payload, options);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(packed, 0, packed.Length);
        return output.ToArray();
    }

    public static MessagePackGzipDecodeResult<T> TryDecode<T>(
        ReadOnlyMemory<byte> bytes,
        MessagePackSerializerOptions options,
        int? maxDecompressedBytes = null
    )
        where T : class
    {
        if (bytes.Length == 0)
            return MessagePackGzipDecodeResult<T>.Failure(MessagePackGzipFailureKind.Empty);
        if (bytes.Length < 2 || bytes.Span[0] != 0x1F || bytes.Span[1] != 0x8B)
            return MessagePackGzipDecodeResult<T>.Failure(MessagePackGzipFailureKind.NotGzip);

        try
        {
            using var input = new MemoryStream(bytes.ToArray(), writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = gzip.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                if (
                    maxDecompressedBytes.HasValue
                    && decompressed.Length + read > maxDecompressedBytes.Value
                )
                {
                    return MessagePackGzipDecodeResult<T>.Failure(
                        MessagePackGzipFailureKind.DecompressedTooLarge
                    );
                }
                decompressed.Write(buffer, 0, read);
            }

            var value = MessagePackSerializer.Deserialize<T>(decompressed.ToArray(), options);
            return value == null
                ? MessagePackGzipDecodeResult<T>.Failure(
                    MessagePackGzipFailureKind.DeserializedNull
                )
                : MessagePackGzipDecodeResult<T>.Success(value);
        }
        catch (Exception ex)
        {
            return MessagePackGzipDecodeResult<T>.Failure(
                MessagePackGzipFailureKind.DecodeFailed,
                ex
            );
        }
    }
}
