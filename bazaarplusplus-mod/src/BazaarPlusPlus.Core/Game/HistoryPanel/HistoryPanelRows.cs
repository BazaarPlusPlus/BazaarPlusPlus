#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

public enum HistoryOutcomeTone
{
    None,
    Won,
    Lost,
}

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

/// <summary>A single board shows the challenger because Ghost Battle payloads use recorder perspective.</summary>
public readonly record struct HistoryBoardTitles(
    string Upper,
    string Lower,
    HistoryOutcomeTone Outcome,
    string OutcomeText
);

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

    // An unfinished Run has no end time, so subtracting its start from the last sighting would overstate play time.
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
