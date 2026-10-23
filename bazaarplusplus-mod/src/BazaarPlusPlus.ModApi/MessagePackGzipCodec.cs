#nullable enable
using MessagePack;
using MessagePack.Resolvers;

namespace BazaarPlusPlus.ModApi;

/// <summary>
/// Compatibility wrapper for local BPP payloads that use contractless MessagePack inside gzip.
/// V6 Run payloads keep their untrusted-data options, size cap, version gate, and closed error codes
/// in <c>RunPayloadV6Codec</c>; only the framing loop is shared internally.
/// </summary>
public static class MessagePackGzipCodec
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            ContractlessStandardResolverAllowPrivate.Instance
        );

    // Every enum, nullable ones included, as its member name: a payload whose enums are BPP card
    // facets must not persist their integers (mod ADR-0015). A file written with integers does
    // not decode with these options.
    private static readonly MessagePackSerializerOptions EnumNameOptions =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                DynamicEnumAsStringResolver.Instance,
                ContractlessStandardResolverAllowPrivate.Instance
            )
        );

    public static byte[] Serialize<T>(T payload, bool enumsAsNames = false)
        where T : class
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        return MessagePackGzipFraming.Encode(payload, enumsAsNames ? EnumNameOptions : Options);
    }

    // The decode exception stays available so callers can tell a runtime binding failure
    // (a missing member in a loaded assembly) from corrupt bytes.
    public static bool TryDeserialize<T>(
        byte[]? payloadBytes,
        out T? value,
        out string? error,
        out Exception? exception,
        int? maxDecompressedBytes = null,
        bool enumsAsNames = false
    )
        where T : class
    {
        value = null;
        error = null;
        exception = null;

        if (payloadBytes == null || payloadBytes.Length == 0)
        {
            error = "payload_empty";
            return false;
        }

        var result = MessagePackGzipFraming.TryDecode<T>(
            payloadBytes,
            enumsAsNames ? EnumNameOptions : Options,
            maxDecompressedBytes
        );
        if (result.Succeeded)
        {
            value = result.Value;
            return true;
        }

        exception = result.Exception;
        error = result.FailureKind switch
        {
            MessagePackGzipFailureKind.Empty => "payload_empty",
            MessagePackGzipFailureKind.DecompressedTooLarge => "payload_too_large",
            MessagePackGzipFailureKind.NotGzip => "payload_not_gzip",
            MessagePackGzipFailureKind.DeserializedNull => "payload_deserialized_null",
            _ when result.Exception != null =>
                $"{result.Exception.GetType().Name}: {result.Exception.Message}",
            _ => "payload_decode_failed",
        };
        return false;
    }
}
