#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum RunOutcomeTier
{
    Misfortune,
    Bronze,
    Silver,
    Gold,
    Diamond,
}

internal readonly record struct HistoryRunRowFields(string Name, string Meta, string Stamp);

internal static class HistoryPanelFormatter
{
    // The status word is deliberately absent: RunBadge already encodes
    // completed/abandoned/active, and repeating it cost the row its strongest line.
    public static HistoryRunRowFields RunRowFields(HistoryRunRecord run) =>
        new(
            HistoryPanelHeroPresentation.DisplayName(run.Hero),
            Meta(run),
            FormatTimestamp(run.EndedAtUtc ?? run.LastSeenAtUtc)
        );

    // A finished run's duration is a real play session. An unfinished one has no EndedAtUtc,
    // so the same subtraction measures start-to-last-seen instead — "85:09" for a run left
    // open overnight. Show it only where it means something.
    private static string Meta(HistoryRunRecord run)
    {
        var head = $"{Mode(run)} · {HistoryPanelText.DayBadge(run.FinalDay)}";
        if (run.EndedAtUtc is not { } ended)
            return head;
        var span = ended - run.StartedAtUtc;
        return $"{head} · {HistoryPanelText.DurationMinutes(Math.Max(0, (int)span.TotalMinutes))}";
    }

    public static string GhostListText(HistoryBattleRecord battle) =>
        $"{battle.OpponentName ?? HistoryPanelText.UnknownOpponent()}\n{GhostDayLine(battle)}\n{FormatTimestamp(battle.RecordedAtUtc)}";

    private static string GhostDayLine(HistoryBattleRecord battle) =>
        battle.IsFinalBattle
            ? $"{HistoryPanelText.DayBadge(battle.Day)} · {HistoryPanelText.FinalBattle()}"
            : HistoryPanelText.DayBadge(battle.Day);

    private static string Mode(HistoryRunRecord run) =>
        run.GameMode.Trim().ToLowerInvariant() switch
        {
            "ranked" => LocalizedTextHelpers.Resolve(new LocalizedTextSet("Ranked", "排位")),
            "unranked" => HistoryPanelText.Unranked(),
            _ => HistoryPanelText.Unknown(),
        };

    public static string PageRange(HistoryCursor? first, HistoryCursor? last) =>
        first.HasValue
        && last.HasValue
        && DateTimeOffset.TryParse(first.Value.Time, out var start)
        && DateTimeOffset.TryParse(last.Value.Time, out var end)
            ? $"{FormatTimestamp(start)} → {FormatTimestamp(end)}"
            : HistoryPanelText.Unknown();

    public static string ShortenRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return HistoryPanelText.UnknownRun();

        return runId.Length <= 14 ? runId : runId[..14];
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

    public static bool IsBattleWin(HistoryBattleRecord battle) =>
        HistoryPanelGhostBattleFilter.ResolveOutcome(battle) == HistoryPanelGhostBattleOutcome.Won;

    public static bool IsBattleLoss(HistoryBattleRecord battle) =>
        HistoryPanelGhostBattleFilter.ResolveOutcome(battle) == HistoryPanelGhostBattleOutcome.Lost;

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("g", ResolveTimestampCulture());
    }

    private static CultureInfo ResolveTimestampCulture()
    {
        var languageCode = L.CurrentLanguageCode?.Trim().Replace('_', '-') ?? string.Empty;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            languageCode = L.CurrentMode == BppChineseLocaleMode.Taiwan ? "zh-TW" : "zh-CN";

        return TryResolveSpecificCulture(languageCode, out var culture)
            ? culture
            : CultureInfo.CurrentCulture;
    }

    private static bool TryResolveSpecificCulture(string cultureName, out CultureInfo culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName);
            if (culture.IsNeutralCulture)
                culture = CultureInfo.CreateSpecificCulture(cultureName);
            return true;
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.CurrentCulture;
            return false;
        }
    }
}
