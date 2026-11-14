#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

// A battle's outcome from the local player's side; hosts colour it from their own theme.
public enum HistoryOutcomeTone
{
    None,
    Won,
    Lost,
}

// The art a row shows at its right edge: a finished run's trophy, an unfinished run's state, or
// a battle's outcome. Each host maps it to its own sprites.
public enum HistoryRowBadge
{
    None,
    StateCompleted,
    StateAbandoned,
    StateActive,
    Bronze,
    Silver,
    Gold,
    Diamond,
    BattleWon,
    BattleLost,
}

/// <summary>
/// One archive row, the same on both hosts: a run in Runs, a battle in Ghost and Favorites.
/// <see cref="BadgeText"/> stands in for missing badge art. <see cref="Rating"/> is empty unless
/// the rank is Legendary; the host tints <see cref="RatingDelta"/> by <see cref="RatingTone"/>.
/// </summary>
public sealed record HistoryArchiveRow(
    string Id,
    string? HeroId,
    string Name,
    string Meta,
    string Stamp,
    string? Rank,
    string RankText,
    string Rating,
    string RatingDelta,
    HistoryOutcomeTone RatingTone,
    HistoryRowBadge Badge,
    string BadgeText,
    HistoryOutcomeTone Outcome,
    string OutcomeText
);

// One battle of the selected run on the timeline beside the boards.
public sealed record HistoryTimelineItem(
    string Id,
    string DayText,
    string OpponentName,
    string? HeroId,
    string? Rank,
    string Rating,
    HistoryOutcomeTone Outcome,
    string OutcomeText
);

/// <summary>
/// The board titles. With two boards <see cref="Upper"/> names the opponent and
/// <see cref="Lower"/> is yours, followed by the outcome. With one board (Ghost, a Ghost-origin
/// favorite) only <see cref="Upper"/> is shown and it names the challenger, wherever the host
/// puts that board.
/// </summary>
public readonly record struct HistoryBoardTitles(
    string Upper,
    string Lower,
    HistoryOutcomeTone Outcome,
    string OutcomeText
);

/// <summary>
/// Builds the rows, timeline and titles both hosts render. The host supplies the hero names
/// (the game's own, which only it can read) and the clock.
/// </summary>
public static class HistoryPanelRows
{
    public static HistoryArchiveRow Run(
        HistoryRunRecord run,
        Func<string?, string> heroName,
        DateTimeOffset now
    )
    {
        var tier = HistoryPanelFormatter.GetRunOutcomeTier(run);
        var badge = tier switch
        {
            RunOutcomeTier.Bronze => HistoryRowBadge.Bronze,
            RunOutcomeTier.Silver => HistoryRowBadge.Silver,
            RunOutcomeTier.Gold => HistoryRowBadge.Gold,
            RunOutcomeTier.Diamond => HistoryRowBadge.Diamond,
            _ => StateBadge(run.RawStatus),
        };
        var (rating, delta, tone) = Rating(run.PlayerRank, run.PlayerRating, run.PlayerRatingDelta);
        return new HistoryArchiveRow(
            run.RunId,
            run.Hero,
            heroName(run.Hero),
            RunMeta(run),
            HistoryPanelFormatter.RowStamp(run.EndedAtUtc ?? run.LastSeenAtUtc, now),
            run.PlayerRank,
            RankText(run.PlayerRank),
            rating,
            delta,
            tone,
            badge,
            badge switch
            {
                HistoryRowBadge.StateActive => HistoryPanelText.RunActive(),
                HistoryRowBadge.StateAbandoned => HistoryPanelText.RunAbandoned(),
                _ => $"{run.Victories ?? 0}-{run.Losses ?? 0}",
            },
            HistoryOutcomeTone.None,
            string.Empty
        );
    }

    // A Ghost that fought one of the player's favorite builds (FavoriteBuilds) leads its meta
    // line with the mark, so both hosts show it without new art.
    public static HistoryArchiveRow Ghost(
        HistoryBattleRecord battle,
        Func<string?, string> heroName,
        DateTimeOffset now,
        bool favoriteBuild = false
    )
    {
        var row = Battle(
            battle,
            heroName,
            HistoryPanelFormatter.RowStamp(battle.RecordedAtUtc, now)
        );
        return favoriteBuild
            ? row with
            {
                Meta = $"{HistoryPanelText.FavoriteBuildMark()} · {row.Meta}",
            }
            : row;
    }

    // A favorite reads as its battle, with the note's first line, else when it was favorited.
    public static HistoryArchiveRow Favorite(
        HistoryFavoriteRecord favorite,
        Func<string?, string> heroName,
        DateTimeOffset now
    )
    {
        var note = favorite.Note.Split('\n')[0].Trim();
        return Battle(
            favorite.Battle,
            heroName,
            note.Length > 0
                ? note
                : HistoryPanelText.FavoritedOn(
                    HistoryPanelFormatter.RowStamp(favorite.FavoritedAtUtc, now)
                )
        );
    }

    public static HistoryTimelineItem Timeline(HistoryBattleRecord battle)
    {
        var outcome = Outcome(battle);
        return new HistoryTimelineItem(
            battle.BattleId,
            HistoryPanelText.DayBadge(battle.Day),
            OpponentName(battle),
            battle.OpponentHero,
            battle.OpponentRank,
            Rating(battle.OpponentRank, battle.OpponentRating, null).Rating,
            outcome,
            OutcomeText(outcome)
        );
    }

    public static HistoryBoardTitles Titles(bool bothBoards, HistoryBattleRecord? battle)
    {
        var outcome = battle == null ? HistoryOutcomeTone.None : Outcome(battle);
        var opponent = battle == null ? null : OpponentName(battle);
        return new HistoryBoardTitles(
            Titled(
                bothBoards
                    ? HistoryPanelText.BoardOpponentTitle()
                    : HistoryPanelText.BoardChallengerTitle(),
                opponent
            ),
            HistoryPanelText.BoardYouTitle(),
            outcome,
            OutcomeText(outcome)
        );
    }

    public static HistoryOutcomeTone Outcome(HistoryBattleRecord battle) =>
        HistoryPanelGhostBattleFilter.ResolveOutcome(battle) switch
        {
            HistoryPanelGhostBattleOutcome.Won => HistoryOutcomeTone.Won,
            HistoryPanelGhostBattleOutcome.Lost => HistoryOutcomeTone.Lost,
            _ => HistoryOutcomeTone.None,
        };

    public static string OutcomeText(HistoryOutcomeTone outcome) =>
        outcome switch
        {
            HistoryOutcomeTone.Won => HistoryPanelText.BattleWon(),
            HistoryOutcomeTone.Lost => HistoryPanelText.BattleLost(),
            _ => string.Empty,
        };

    private static HistoryArchiveRow Battle(
        HistoryBattleRecord battle,
        Func<string?, string> heroName,
        string stamp
    )
    {
        var outcome = Outcome(battle);
        var hero = heroName(battle.OpponentHero);
        var day = HistoryPanelText.DayBadge(battle.Day);
        if (battle.IsFinalBattle)
            day = $"{day} · {HistoryPanelText.FinalBattle()}";
        var (rating, delta, tone) = Rating(battle.OpponentRank, battle.OpponentRating, null);
        return new HistoryArchiveRow(
            battle.BattleId,
            battle.OpponentHero,
            OpponentName(battle),
            hero.Length == 0 ? day : $"{hero} · {day}",
            stamp,
            battle.OpponentRank,
            RankText(battle.OpponentRank),
            rating,
            delta,
            tone,
            outcome switch
            {
                HistoryOutcomeTone.Won => HistoryRowBadge.BattleWon,
                HistoryOutcomeTone.Lost => HistoryRowBadge.BattleLost,
                _ => HistoryRowBadge.None,
            },
            outcome == HistoryOutcomeTone.None ? "?" : OutcomeText(outcome),
            outcome,
            outcome == HistoryOutcomeTone.None ? HistoryPanelText.Unknown() : OutcomeText(outcome)
        );
    }

    // The status word is absent: the badge already says completed, abandoned or active. A
    // finished run's duration is a real play session; an unfinished one has no end, so the same
    // subtraction would measure start to last seen ("85 h" for a run left open overnight).
    private static string RunMeta(HistoryRunRecord run)
    {
        var mode = run.GameMode.Trim().ToLowerInvariant() switch
        {
            "ranked" => HistoryPanelText.Ranked(),
            "unranked" => HistoryPanelText.Unranked(),
            _ => HistoryPanelText.Unknown(),
        };
        var head = $"{mode} · {HistoryPanelText.DayBadge(run.FinalDay)}";
        return run.EndedAtUtc is { } ended
            ? $"{head} · {HistoryPanelText.Duration(ended - run.StartedAtUtc)}"
            : head;
    }

    private static HistoryRowBadge StateBadge(string status) =>
        status switch
        {
            "active" => HistoryRowBadge.StateActive,
            "abandoned" => HistoryRowBadge.StateAbandoned,
            _ => HistoryRowBadge.StateCompleted,
        };

    private static string RankText(string? rank) =>
        string.IsNullOrWhiteSpace(rank) ? "?" : HistoryPanelText.RankName(rank!);

    // Only a Legendary rank has a rating. "1520 +16": an unknown change shows the rating alone
    // rather than a misleading +0, and a known zero reads ±0.
    private static (string Rating, string Delta, HistoryOutcomeTone Tone) Rating(
        string? rank,
        int? rating,
        int? delta
    )
    {
        if (
            !string.Equals(rank?.Trim(), "Legendary", StringComparison.OrdinalIgnoreCase)
            || rating is not { } value
        )
            return (string.Empty, string.Empty, HistoryOutcomeTone.None);
        var text = value.ToString(CultureInfo.CurrentCulture);
        if (delta is not { } change)
            return (text, string.Empty, HistoryOutcomeTone.None);
        return (
            text,
            change.ToString("+0;-0;±0", CultureInfo.InvariantCulture),
            change > 0 ? HistoryOutcomeTone.Won
            : change < 0 ? HistoryOutcomeTone.Lost
            : HistoryOutcomeTone.None
        );
    }

    private static string OpponentName(HistoryBattleRecord battle) =>
        string.IsNullOrWhiteSpace(battle.OpponentName)
            ? HistoryPanelText.UnknownOpponent()
            : battle.OpponentName!;

    private static string Titled(string side, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? side : $"{side} · {detail}";
}
