#nullable enable
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal static class GhostBattlePayloadCodec
{
    public static byte[] Serialize(GhostBattlePayload payload) =>
        MessagePackGzipCodec.Serialize(payload);

    public static bool TryDeserialize(
        byte[]? payloadBytes,
        out GhostBattlePayload? payload,
        out string? error
    ) => MessagePackGzipCodec.TryDeserialize(payloadBytes, out payload, out error);
}
