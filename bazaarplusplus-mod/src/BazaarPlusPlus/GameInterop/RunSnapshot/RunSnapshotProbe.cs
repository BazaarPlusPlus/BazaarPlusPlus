#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.RunSnapshot;

internal sealed class RunSnapshotProbe : IRunSnapshotProbe
{
    public bool TryGetRunBasics(out RunBasicsSnapshot basics)
    {
        basics = null!;
        var run = Data.Run;
        if (run == null)
            return false;

        basics = new RunBasicsSnapshot
        {
            Day = (int?)run.Day,
            Hour = (int?)run.Hour,
            Victories = unchecked((int)run.Victories),
            Losses = unchecked((int)run.Losses),
            Hero = run.Player?.Hero.ToString(),
            GameMode = Data.SelectedPlayMode.ToString(),
        };
        return true;
    }

    public bool TryGetPlayerStats(out PlayerStatsSnapshot stats)
    {
        stats = null!;
        var player = Data.Run?.Player;
        if (player == null)
            return false;

        stats = new PlayerStatsSnapshot
        {
            MaxHealth = player.GetAttributeValue(EPlayerAttributeType.HealthMax),
            Prestige = player.GetAttributeValue(EPlayerAttributeType.Prestige),
            Level = player.GetAttributeValue(EPlayerAttributeType.Level),
            Income = player.GetAttributeValue(EPlayerAttributeType.Income),
            Gold = player.GetAttributeValue(EPlayerAttributeType.Gold),
        };
        return true;
    }

    public bool TryGetRankSnapshot(out RankSnapshot rank)
    {
        rank = null!;
        if (!BppClientCacheBridge.TryGetPlayerRankSnapshot(out var name, out var rating))
            return false;

        rank = new RankSnapshot { Rank = name, Rating = rating };
        return true;
    }

    public bool TryGetLeaderboardPosition(out int? position) =>
        BppClientCacheBridge.TryGetPlayerLeaderboardPosition(out position);
}
