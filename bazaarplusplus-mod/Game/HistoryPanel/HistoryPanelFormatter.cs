#nullable enable
using System;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum RunOutcomeTier
{
    Misfortune,
    Bronze,
    Silver,
    Gold,
    Diamond,
}

internal static class HistoryPanelFormatter
{
    public static string ShortenRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return HistoryPanelText.UnknownRun();

        return runId.Length <= 14 ? runId : runId[..14];
    }

    public static string FormatRunRecord(HistoryRunRecord run)
    {
        return run.Victories.HasValue || run.Losses.HasValue
            ? HistoryPanelText.RunRecord(run.Victories ?? 0, run.Losses ?? 0)
            : "-";
    }

    public static RunOutcomeTier? GetRunOutcomeTier(HistoryRunRecord run)
    {
        if (!string.Equals(run.RawStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return null;

        var wins = run.Victories ?? 0;
        var losses = run.Losses ?? 0;
        var totalBattles = wins + losses;

        if (wins == 10 && totalBattles == 10)
            return RunOutcomeTier.Diamond;

        if (wins >= 10 && totalBattles > 10)
            return RunOutcomeTier.Gold;

        if (wins >= 7)
            return RunOutcomeTier.Silver;

        if (wins >= 4)
            return RunOutcomeTier.Bronze;

        return RunOutcomeTier.Misfortune;
    }

    public static string FormatRunStatus(string? rawStatus)
    {
        return rawStatus switch
        {
            "completed" => HistoryPanelText.Completed(),
            "abandoned" => HistoryPanelText.Abandoned(),
            "active" => HistoryPanelText.Active(),
            null => HistoryPanelText.Unknown(),
            _ => char.ToUpperInvariant(rawStatus[0]) + rawStatus[1..],
        };
    }

    public static string FormatBattleResult(HistoryBattleRecord battle)
    {
        if (string.IsNullOrWhiteSpace(battle.Result))
            return HistoryPanelText.Unknown();

        return IsBattleWin(battle) ? HistoryPanelText.Win()
            : IsBattleLoss(battle) ? HistoryPanelText.Loss()
            : battle.Result;
    }

    public static bool IsBattleWin(HistoryBattleRecord battle)
    {
        return string.Equals(battle.Result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Won", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBattleLoss(HistoryBattleRecord battle)
    {
        return string.Equals(battle.Result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Lost", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGhostOpponentEliminated(HistoryBattleRecord? battle)
    {
        if (battle == null || battle.Source != HistoryBattleSource.Ghost)
            return false;

        return battle.IsBundleFinalBattle && IsBattleWinFromLocalPerspective(battle);
    }

    private static bool IsBattleWinFromLocalPerspective(HistoryBattleRecord battle)
    {
        return IsBattleWin(battle)
            || string.Equals(battle.WinnerCombatantId, "Player", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FormatOpponentHero(string? rawHero)
    {
        if (string.IsNullOrWhiteSpace(rawHero))
            return null;

        return rawHero;
    }

    public static string FormatDayOnly(int? day)
    {
        return HistoryPanelText.DayBadge(day);
    }

    public static string FormatDayHour(int? day, int? hour)
    {
        return HistoryPanelText.DayHourBadge(day, hour);
    }

    public static string? FormatRunDuration(HistoryRunRecord run)
    {
        if (!string.Equals(run.RawStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!run.EndedAtUtc.HasValue)
            return null;

        var duration = run.EndedAtUtc.Value - run.StartedAtUtc;
        if (duration <= TimeSpan.Zero)
            return null;

        if (duration.TotalHours >= 1d)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        if (duration.TotalMinutes >= 1d)
            return $"{Mathf.Max(1, Mathf.RoundToInt((float)duration.TotalMinutes))}m";

        return $"{Mathf.Max(1, duration.Seconds)}s";
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("MM-dd HH:mm");
    }

    public static string? NormalizeRank(string? rawRank)
    {
        if (string.IsNullOrWhiteSpace(rawRank))
            return null;

        var trimmed = rawRank.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }
}
