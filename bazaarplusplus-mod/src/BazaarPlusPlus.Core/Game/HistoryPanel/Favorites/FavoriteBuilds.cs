#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel.Favorites;

/// <summary>The game and Bundle omit the opponent snapshot's Run ID, so matching a favorite uses a build fingerprint.</summary>
public sealed class FavoriteBuilds
{
    // In a 2026-11-14 sample, only 1 of 20 Ghost Battles before day 5 uniquely matched a Run, compared with 34 of 43 from day 5 on.
    internal const int MinimumDay = 5;

    // The challenger's clock recorded the Ghost and this machine's clock the favorite.
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(10);

    public static readonly FavoriteBuilds None = new(Array.Empty<Build>());

    private readonly IReadOnlyList<Build> _builds;

    private FavoriteBuilds(IReadOnlyList<Build> builds) => _builds = builds;

    public int Count => _builds.Count;

    public static FavoriteBuilds From(IEnumerable<HistoryBattleRecord> favorites)
    {
        var builds = new List<Build>();
        foreach (var favorite in favorites)
            if (Build.Of(favorite, HistoryBattleSource.Local) is { } build)
                builds.Add(build);
        return builds.Count == 0 ? None : new FavoriteBuilds(builds);
    }

    public bool Matches(HistoryBattleRecord ghost)
    {
        if (_builds.Count == 0 || Build.Of(ghost, HistoryBattleSource.Ghost) is not { } fought)
            return false;
        foreach (var build in _builds)
            if (build.SameAs(fought) && ghost.RecordedAtUtc >= build.RecordedAtUtc - ClockSkew)
                return true;
        return false;
    }

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
