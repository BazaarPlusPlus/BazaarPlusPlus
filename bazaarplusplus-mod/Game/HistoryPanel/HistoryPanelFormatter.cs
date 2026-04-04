#nullable enable
using System;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFormatter
{
    public static string ShortenRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return "Unknown Run";

        return runId.Length <= 14 ? runId : runId[..14];
    }

    public static string FormatRunRecord(HistoryRunRecord run)
    {
        return run.Victories.HasValue || run.Losses.HasValue
            ? $"{run.Victories ?? 0}W - {run.Losses ?? 0}L"
            : "-";
    }

    public static string? FormatRunAchievement(HistoryRunRecord run)
    {
        if (!string.Equals(run.RawStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return null;

        var wins = run.Victories ?? 0;
        var losses = run.Losses ?? 0;
        var totalBattles = wins + losses;

        if (wins == 10 && totalBattles == 10)
            return "PERFECT";

        if (wins >= 10 && totalBattles > 10)
            return "GOLD";

        if (wins >= 7)
            return "SILVER";

        if (wins >= 4)
            return "BRONZE";

        return "MISFORTUNE";
    }

    public static string FormatRunStatus(string? rawStatus)
    {
        return rawStatus switch
        {
            "completed" => "Completed",
            "abandoned" => "Abandoned",
            "active" => "Active",
            null => "Unknown",
            _ => char.ToUpperInvariant(rawStatus[0]) + rawStatus[1..],
        };
    }

    public static string FormatBattleResult(HistoryBattleRecord battle)
    {
        if (string.IsNullOrWhiteSpace(battle.Result))
            return "Unknown";

        return string.Equals(battle.Result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Won", StringComparison.OrdinalIgnoreCase)
                ? "Win"
            : string.Equals(battle.Result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Lost", StringComparison.OrdinalIgnoreCase)
                ? "Loss"
            : battle.Result;
    }

    public static string? FormatOpponentHero(string? rawHero)
    {
        if (string.IsNullOrWhiteSpace(rawHero))
            return null;

        return rawHero;
    }

    public static string FormatDayOnly(int? day)
    {
        return day.HasValue ? $"D{day.Value}" : "D?";
    }

    public static string FormatDayHour(int? day, int? hour)
    {
        var dayText = day.HasValue ? $"D{day.Value}" : "D?";
        var hourText = hour.HasValue ? $"H{hour.Value}" : "H?";
        return $"{dayText} {hourText}";
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
}
