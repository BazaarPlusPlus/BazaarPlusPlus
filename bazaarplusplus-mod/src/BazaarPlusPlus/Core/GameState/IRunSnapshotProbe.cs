#nullable enable
namespace BazaarPlusPlus.Core.GameState;

internal interface IRunSnapshotProbe
{
    bool TryGetRunBasics(out RunBasicsSnapshot basics);

    bool TryGetPlayerStats(out PlayerStatsSnapshot stats);

    bool TryGetRankSnapshot(out RankSnapshot rank);

    bool TryGetLeaderboardPosition(out int? position);
}
