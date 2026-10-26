#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

public static class GhostBattlePayloadReader
{
    public static bool MatchesIdentity(
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
}
