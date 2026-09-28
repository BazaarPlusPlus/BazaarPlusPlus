#nullable enable
using System.Reflection;
using BazaarPlusPlus.Localization;

internal static class PageRangeTests
{
    public static void Run(Assembly assembly)
    {
        var pageType = assembly
            .GetType("BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPage`1", true)!
            .MakeGenericType(typeof(object));
        var cursorType = assembly.GetType(
            "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryCursor",
            true
        )!;
        var formatter = assembly
            .GetType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelFormatter", true)!
            .GetMethod("PageRange")!
            .MakeGenericMethod(typeof(object));
        var first = Activator.CreateInstance(cursorType, "2026-09-20T00:00:00Z", "first")!;
        var last = Activator.CreateInstance(cursorType, "2026-09-19T00:00:00Z", "last")!;
        try
        {
            foreach (var language in new[] { "en", "zh-Hans" })
            {
                L.Install(new Language(language), new Mainland());
                var chinese = language == "zh-Hans";
                foreach (var ghost in new[] { false, true })
                {
                    var noun = ghost ? "Battles" : "Runs";
                    var unit = ghost ? "场" : "局";
                    var text = Format(Page(40, 41, 213, true), ghost);
                    Require(
                        text.StartsWith(
                            chinese
                                ? $"第 41–80 {unit} · 共 213 {unit}\n"
                                : $"{noun} 41–80 · 213 total\n",
                            StringComparison.Ordinal
                        ),
                        "The page must show its one-based range and filtered total before the time range."
                    );
                    Require(
                        text.Contains(" → ", StringComparison.Ordinal)
                            && text.Split('\n').Length == 2,
                        "Valid timestamps must remain on a second line."
                    );
                    var empty = Format(Page(0, 0, 0, false), ghost);
                    Require(
                        empty
                            == (
                                chinese ? $"共 0 {unit}" : $"0 {(ghost ? "battles" : "runs")} total"
                            ),
                        "Empty filters must show zero without an invented range or unknown timestamp."
                    );
                    var outside = Format(Page(0, 0, 213, false), ghost);
                    Require(
                        outside.Contains("213", StringComparison.Ordinal) && !outside.Contains('–'),
                        "An empty cursor result must retain the filtered population without a range."
                    );
                    var finalPage = Format(Page(1, 213, 213, false), ghost);
                    Require(
                        finalPage.Contains("213–213", StringComparison.Ordinal),
                        "A one-record final page must use its actual bounds."
                    );
                    var large = Format(Page(40, 2147483648, 2147484000, false), ghost);
                    Require(
                        large.Contains("2147483648–2147483687", StringComparison.Ordinal),
                        "SQLite's 64-bit counts must not overflow during formatting."
                    );
                }
            }
        }
        finally
        {
            L.Install(new Language("en"), new Mainland());
        }

        object Page(int count, long position, long total, bool timestamps)
        {
            var page = Activator.CreateInstance(
                pageType,
                new object[count],
                timestamps ? first : null,
                timestamps ? last : null,
                false,
                false
            )!;
            pageType.GetProperty("FirstPosition")!.SetValue(page, position);
            pageType.GetProperty("TotalCount")!.SetValue(page, total);
            return page;
        }
        string Format(object page, bool ghost) => (string)formatter.Invoke(null, [page, ghost])!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Language(string code) : ILanguageProvider
    {
        public string CurrentLanguageCode => code;
    }

    private sealed class Mainland : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
