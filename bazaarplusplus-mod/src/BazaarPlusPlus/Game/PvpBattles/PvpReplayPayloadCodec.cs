#nullable enable
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.PvpBattles;

internal static class PvpReplayPayloadCodec
{
    public static byte[] Serialize(PvpReplayPayload payload) =>
        MessagePackGzipCodec.Serialize(payload);

    public static bool TryDeserialize(
        byte[]? payloadBytes,
        out PvpReplayPayload? payload,
        out string? error
    ) => MessagePackGzipCodec.TryDeserialize(payloadBytes, out payload, out error);
}
