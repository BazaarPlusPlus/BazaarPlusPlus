#nullable enable
using MessagePack;
using MessagePack.Resolvers;

namespace BazaarPlusPlus.ModApi.Bundle;

public static class RunPayloadV5Codec
{
    public const int MaxDecompressedBytes = 64 * 1024 * 1024;

    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions
        .Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        .WithSecurity(MessagePackSecurity.UntrustedData);

    public static byte[] Encode(RunPayloadV5 payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.PayloadFormatVersion != BundleLimitsV5.RunFormatVersion)
            throw new ArgumentException("Run payload format version must be 5.", nameof(payload));

        return MessagePackGzipFraming.Encode(payload, Options);
    }

    public static RunPayloadV5 Decode(ReadOnlyMemory<byte> bytes)
    {
        if (!TryDecode(bytes, out var payload, out var reason))
            throw new InvalidDataException($"Run payload is invalid ({reason}).");
        return payload!;
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> bytes,
        out RunPayloadV5? payload,
        out string? reason
    )
    {
        payload = null;
        reason = null;
        if (bytes.Length < 2)
        {
            reason = "run_payload_empty";
            return false;
        }
        var source = bytes.Span;
        if (source[0] != 0x1F || source[1] != 0x8B)
        {
            reason = "run_payload_not_gzip";
            return false;
        }

        var result = MessagePackGzipFraming.TryDecode<RunPayloadV5>(
            bytes,
            Options,
            MaxDecompressedBytes
        );
        if (!result.Succeeded)
        {
            reason = result.FailureKind switch
            {
                MessagePackGzipFailureKind.Empty => "run_payload_empty",
                MessagePackGzipFailureKind.NotGzip => "run_payload_not_gzip",
                MessagePackGzipFailureKind.DecompressedTooLarge =>
                    "run_payload_decompressed_too_large",
                MessagePackGzipFailureKind.DeserializedNull => "run_payload_deserialized_null",
                _ => "run_payload_decode_failed",
            };
            return false;
        }

        payload = result.Value;
        if (payload!.PayloadFormatVersion != BundleLimitsV5.RunFormatVersion)
        {
            payload = null;
            reason = "unsupported_run_payload_version";
            return false;
        }
        return true;
    }
}
