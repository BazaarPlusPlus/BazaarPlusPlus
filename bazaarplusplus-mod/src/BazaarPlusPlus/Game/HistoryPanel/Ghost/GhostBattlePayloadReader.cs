#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal static class GhostBattlePayloadReader
{
    internal static GhostBattlePayload? Normalize(GhostBattlePayload? payload)
    {
        if (payload == null || payload.PerspectiveVersion != 0)
            return payload;

        GhostManifestProjection.SwapPerspective(payload.BattleManifest);
        payload.PerspectiveVersion = 1;
        return payload;
    }
}
