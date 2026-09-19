#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal static class GhostBattlePayloadReader
{
    internal static bool MatchesIdentity(
        GhostBattlePayload? payload,
        string id,
        string account,
        string uploader
    ) =>
        payload is { PerspectiveVersion: 1 }
        && payload.BattleId == id
        && payload.BattleManifest?.BattleId == id
        && payload.ReplayPayload?.BattleId == id
        && payload.BattleManifest.Participants?.OpponentAccountId == account
        && payload.BattleManifest.Participants?.PlayerAccountId == uploader;

    internal static GhostBattlePayload? Normalize(GhostBattlePayload? payload)
    {
        if (payload == null || payload.PerspectiveVersion != 0)
            return payload;

        GhostManifestProjection.SwapPerspective(payload.BattleManifest);
        payload.PerspectiveVersion = 1;
        return payload;
    }
}
