#nullable enable
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

// A ghost payload's card facets are stored by member name (ADR-0015 §4).
public static class GhostBattlePayloadCodec
{
    public static byte[] Serialize(GhostBattlePayload payload) =>
        MessagePackGzipCodec.Serialize(payload, enumsAsNames: true);

    public static bool TryDeserialize(
        byte[]? payloadBytes,
        out GhostBattlePayload? payload,
        out string? error,
        out Exception? exception
    ) =>
        MessagePackGzipCodec.TryDeserialize(
            payloadBytes,
            out payload,
            out error,
            out exception,
            64 * 1024 * 1024,
            enumsAsNames: true
        );
}
