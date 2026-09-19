#nullable enable
using BazaarPlusPlus.ModApi;

internal static class CodecTests
{
    // Plain POCO: ContractlessStandardResolverAllowPrivate needs no MessagePack attributes.
    private sealed class Sample
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public int[] Values { get; set; } = Array.Empty<int>();
    }

    public static void Run()
    {
        RoundTripsPayload();
        ProducesGzipFraming();
        ReportsErrorForEmptyOrNonGzip();
        ReportsErrorForCorruptGzip();
        Console.WriteLine("CodecTests passed.");
    }

    private static void RoundTripsPayload()
    {
        var sample = new Sample
        {
            Name = "abc",
            Count = 7,
            Values = new[] { 1, 2, 3 },
        };
        var bytes = MessagePackGzipCodec.Serialize(sample);
        var ok = MessagePackGzipCodec.TryDeserialize<Sample>(
            bytes,
            out var restored,
            out var error
        );
        if (
            !ok
            || error != null
            || restored == null
            || restored.Name != "abc"
            || restored.Count != 7
            || !restored.Values.SequenceEqual(new[] { 1, 2, 3 })
        )
            throw new Exception("MessagePackGzipCodec round-trip mismatch.");
    }

    private static void ProducesGzipFraming()
    {
        var bytes = MessagePackGzipCodec.Serialize(new Sample { Name = "x" });
        if (bytes.Length < 2 || bytes[0] != 0x1F || bytes[1] != 0x8B)
            throw new Exception("MessagePackGzipCodec did not emit gzip framing.");
    }

    private static void ReportsErrorForEmptyOrNonGzip()
    {
        AssertDeserializeFailure(null, "payload_empty");
        AssertDeserializeFailure(Array.Empty<byte>(), "payload_empty");
        AssertDeserializeFailure(new byte[] { 1, 2, 3, 4 }, "payload_not_gzip");
    }

    private static void ReportsErrorForCorruptGzip()
    {
        AssertDeserializeFailure(new byte[] { 0x1F, 0x8B, 0x01, 0x02 }, "Exception:");
    }

    private static void AssertDeserializeFailure(byte[]? bytes, string expectedErrorFragment)
    {
        if (MessagePackGzipCodec.TryDeserialize<Sample>(bytes, out var restored, out var error))
            throw new Exception("Expected MessagePackGzipCodec deserialization to fail.");
        if (restored != null)
            throw new Exception("Expected failed deserialization to leave value null.");
        if (string.IsNullOrWhiteSpace(error) || !error.Contains(expectedErrorFragment))
            throw new Exception(
                $"Expected error containing '{expectedErrorFragment}', got '{error ?? "<null>"}'."
            );
    }
}
