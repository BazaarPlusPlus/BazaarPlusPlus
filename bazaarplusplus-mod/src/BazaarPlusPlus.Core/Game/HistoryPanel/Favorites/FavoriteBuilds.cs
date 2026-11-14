#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel.Favorites;

/// <summary>
/// The player's favorited builds, read to mark the Ghost Battles that fought one of them. Neither
/// the game nor a Bundle names the Run an opponent snapshot came from, so a Ghost matches a
/// favorite of the player's own battle when both sides of the comparison agree on the player's
/// hero, day, level, prestige and victories, and the Ghost was recorded after the favorite.
/// </summary>
public sealed class FavoriteBuilds
{
    // Days 1-4 builds look alike: in a 2026-11-14 production sample only 1 of 20 early-day Ghosts
    // left a single candidate Run, against 34 of 43 from day 5 on.
    internal const int MinimumDay = 5;

    // The challenger's clock recorded the Ghost and this machine's clock the favorite.
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(10);

    public static readonly FavoriteBuilds None = new(Array.Empty<Build>());

    private readonly IReadOnlyList<Build> _builds;

    private FavoriteBuilds(IReadOnlyList<Build> builds) => _builds = builds;

    public int Count => _builds.Count;

    /// <summary>The builds of favorites of the player's own battles; others are skipped.</summary>
    public static FavoriteBuilds From(IEnumerable<HistoryBattleRecord> favorites)
    {
        var builds = new List<Build>();
        foreach (var favorite in favorites)
            if (Build.Of(favorite, HistoryBattleSource.Local) is { } build)
                builds.Add(build);
        return builds.Count == 0 ? None : new FavoriteBuilds(builds);
    }

    /// <summary>Whether a Ghost Battle, read in the local perspective, fought a favorite build.</summary>
    public bool Matches(HistoryBattleRecord ghost)
    {
        if (_builds.Count == 0 || Build.Of(ghost, HistoryBattleSource.Ghost) is not { } fought)
            return false;
        foreach (var build in _builds)
            if (build.SameAs(fought) && ghost.RecordedAtUtc >= build.RecordedAtUtc - ClockSkew)
                return true;
        return false;
    }

    // The local player's side of a battle; a Ghost record is already projected to it.
    private sealed record Build(
        string Hero,
        int Day,
        int Level,
        int Prestige,
        int Victories,
        DateTimeOffset RecordedAtUtc
    )
    {
        public static Build? Of(HistoryBattleRecord battle, HistoryBattleSource source) =>
            battle.Source == source
            && battle.PlayerHero?.Trim() is { Length: > 0 } hero
            && battle.Day is { } day and >= MinimumDay
            && battle.PlayerLevel is { } level
            && battle.PlayerPrestige is { } prestige
            && battle.PlayerVictories is { } victories
                ? new Build(hero, day, level, prestige, victories, battle.RecordedAtUtc)
                : null;

        public bool SameAs(Build other) =>
            string.Equals(Hero, other.Hero, StringComparison.OrdinalIgnoreCase)
            && Day == other.Day
            && Level == other.Level
            && Prestige == other.Prestige
            && Victories == other.Victories;
    }
}
