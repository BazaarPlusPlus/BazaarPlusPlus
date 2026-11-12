#nullable enable
using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public enum RunOutcomeTier
{
    Misfortune,
    Bronze,
    Silver,
    Gold,
    Diamond,
}

public static class HistoryPanelFormatter
{
    public static string PageRange<T>(HistoryCountedPage<T> page, bool ghost = false)
    {
        var counts =
            page.Rows.Count == 0
                ? HistoryPanelText.PageTotal(page.TotalCount, ghost)
                : HistoryPanelText.PagePosition(
                    page.FirstPosition,
                    page.LastPosition,
                    page.TotalCount,
                    ghost
                );
        return
            page.First.HasValue
            && page.Last.HasValue
            && TryFromUnixMilliseconds(page.First.Value.Time, out var start)
            && TryFromUnixMilliseconds(page.Last.Value.Time, out var end)
            ? $"{counts}\n{FormatTimestamp(start)} → {FormatTimestamp(end)}"
            : counts;
    }

    private static bool TryFromUnixMilliseconds(long milliseconds, out DateTimeOffset value)
    {
        var valid =
            milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            && milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
        value = valid ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : default;
        return valid;
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

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("g", ResolveTimestampCulture());
    }

    // A row's stamp: month, day and time, with the year only when it is not this year's.
    public static string RowStamp(DateTimeOffset value, DateTimeOffset now)
    {
        var local = value.ToLocalTime();
        return local.ToString(
            local.Year == now.ToLocalTime().Year ? "MM/dd HH:mm" : "yyyy/MM/dd HH:mm",
            ResolveTimestampCulture()
        );
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
