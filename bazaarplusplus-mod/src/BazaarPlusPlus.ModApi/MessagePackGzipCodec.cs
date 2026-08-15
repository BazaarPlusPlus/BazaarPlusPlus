#nullable enable
using MessagePack;
using MessagePack.Resolvers;

namespace BazaarPlusPlus.ModApi;

/// <summary>
/// Compatibility wrapper for local BPP payloads that use contractless MessagePack inside gzip.
/// V5 Run payloads keep their untrusted-data options, size cap, version gate, and closed error codes
/// in <c>RunPayloadV5Codec</c>; only the framing loop is shared internally.
/// </summary>
public static class MessagePackGzipCodec
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            ContractlessStandardResolverAllowPrivate.Instance
        );

    public static byte[] Serialize<T>(T payload)
        where T : class
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        return MessagePackGzipFraming.Encode(payload, Options);
    }

    public static bool TryDeserialize<T>(byte[]? payloadBytes, out T? value, out string? error)
        where T : class
    {
        value = null;
        error = null;

        if (payloadBytes == null || payloadBytes.Length == 0)
        {
            error = "payload_empty";
            return false;
        }

        var result = MessagePackGzipFraming.TryDecode<T>(payloadBytes, Options);
        if (result.Succeeded)
        {
            value = result.Value;
            return true;
        }

        error = result.FailureKind switch
        {
            MessagePackGzipFailureKind.Empty => "payload_empty",
            MessagePackGzipFailureKind.NotGzip => "payload_not_gzip",
            MessagePackGzipFailureKind.DeserializedNull => "payload_deserialized_null",
            _ when result.Exception != null =>
                $"{result.Exception.GetType().Name}: {result.Exception.Message}",
            _ => "payload_decode_failed",
        };
        return false;
    }
}
