using BazaarPlusPlus.Core.GameState;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The run facts Run Logging asks for, read from the IL2CPP game state: the Android counterpart of
/// the desktop's <c>RunSnapshotProbe</c>, returning the same BPP snapshots. Main thread only.
/// </summary>
internal sealed class AndroidRunSnapshotProbe : IRunSnapshotProbe
{
    public bool TryGetRunBasics(out RunBasicsSnapshot basics)
    {
        basics = null!;
        var run = Data.Run;
        if (run == null)
            return false;

        basics = new RunBasicsSnapshot
        {
            Day = unchecked((int)run.Day),
            Hour = unchecked((int)run.Hour),
            Victories = unchecked((int)run.Victories),
            Losses = unchecked((int)run.Losses),
            Hero = run.Player is { } player ? AndroidGameIds.Hero(player.Hero) : null,
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
            MaxHealth = AndroidGameIds.Attribute(player, EPlayerAttributeType.HealthMax),
            Prestige = AndroidGameIds.Attribute(player, EPlayerAttributeType.Prestige),
            Level = AndroidGameIds.Attribute(player, EPlayerAttributeType.Level),
            Income = AndroidGameIds.Attribute(player, EPlayerAttributeType.Income),
            Gold = AndroidGameIds.Attribute(player, EPlayerAttributeType.Gold),
        };
        return true;
    }

    public bool TryGetRankSnapshot(out RankSnapshot rank)
    {
        rank = null!;
        if (!AndroidClientCache.TryGetRank(out var name, out var rating))
            return false;
        rank = new RankSnapshot { Rank = name, Rating = rating };
        return true;
    }

    public bool TryGetLeaderboardPosition(out int? position)
    {
        position = null;
        return false;
    }
}
