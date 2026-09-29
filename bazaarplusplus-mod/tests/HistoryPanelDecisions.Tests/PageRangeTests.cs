#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Localization;

internal static class PageRangeTests
{
    private static readonly HistoryCursor First = new("2026-09-20T00:00:00Z", "first");
    private static readonly HistoryCursor Last = new("2026-09-19T00:00:00Z", "last");

    public static void Run()
    {
        CheckShapes();
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
                    var text = Format(Page(40, 41, 213), ghost);
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
                    var empty = Format(HistoryCountedPage<object>.Empty(), ghost);
                    Require(
                        empty
                            == (
                                chinese ? $"共 0 {unit}" : $"0 {(ghost ? "battles" : "runs")} total"
                            ),
                        "Empty filters must show zero without an invented range or unknown timestamp."
                    );
                    var outside = Format(HistoryCountedPage<object>.Empty(213), ghost);
                    Require(
                        outside
                            == (
                                chinese
                                    ? $"共 213 {unit}"
                                    : $"213 {(ghost ? "battles" : "runs")} total"
                            ),
                        "A cursor past either end must retain the filtered population without a range."
                    );
                    var finalPage = Format(Page(1, 213, 213), ghost);
                    Require(
                        finalPage.Contains("213–213", StringComparison.Ordinal),
                        "A one-record final page must use its actual bounds."
                    );
                    var large = Format(Page(40, 2147483648, 2147484000), ghost);
                    Require(
                        large.Contains("2147483648–2147483687", StringComparison.Ordinal),
                        "SQLite's 64-bit counts must not overflow during formatting."
                    );
                    var badTime = Format(
                        new HistoryCountedPage<object>(
                            new object[3],
                            new("not a time", "a"),
                            new("not a time", "b"),
                            1,
                            3
                        ),
                        ghost
                    );
                    Require(
                        !badTime.Contains('\n'),
                        "Unparseable anchor times must leave only the position line."
                    );
                }
            }
        }
        finally
        {
            L.Install(new Language("en"), new Mainland());
        }
    }

    // The two page shapes reject the combinations a reader could not have produced.
    private static void CheckShapes()
    {
        var page = Page(40, 41, 213);
        Require(
            page.HasNewer && page.HasOlder && page.LastPosition == 80,
            "A middle page must derive both neighbours and its last position from its counts."
        );
        var newest = Page(40, 1, 80);
        Require(!newest.HasNewer && newest.HasOlder, "Position one has no newer rows.");
        var oldest = Page(40, 41, 80);
        Require(oldest.HasNewer && !oldest.HasOlder, "The last page has no older rows.");
        foreach (var total in new long[] { 0, 213 })
        {
            var empty = HistoryCountedPage<object>.Empty(total);
            Require(
                empty.Rows.Count == 0
                    && empty.FirstPosition == 0
                    && empty.LastPosition == 0
                    && empty.TotalCount == total
                    && empty.First == null
                    && empty.Last == null
                    && !empty.HasNewer
                    && !empty.HasOlder,
                "An empty counted page has no position, anchors or neighbours."
            );
        }
        var cursorEmpty = HistoryCursorPage<object>.Empty;
        Require(
            cursorEmpty.Rows.Count == 0
                && cursorEmpty.First == null
                && cursorEmpty.Last == null
                && !cursorEmpty.HasNewer
                && !cursorEmpty.HasOlder,
            "An empty cursor page has no anchors or neighbours."
        );
        Throws(() => Page(40, 0, 213), "Rows need a one-based position.");
        Throws(() => Page(40, 200, 213), "Rows must fit inside the total.");
        Throws(() => Page(0, 1, 213), "A counted page with anchors needs rows.");
        Throws(() => HistoryCountedPage<object>.Empty(-1), "A total cannot be negative.");
        Throws(
            () => new HistoryCursorPage<object>(Array.Empty<object>(), First, Last, true, false),
            "A cursor page with anchors needs rows."
        );
    }

    private static HistoryCountedPage<object> Page(int count, long position, long total) =>
        new(new object[count], First, Last, position, total);

    private static string Format(HistoryCountedPage<object> page, bool ghost) =>
        HistoryPanelFormatter.PageRange(page, ghost);

    private static void Throws(Func<object> build, string message)
    {
        try
        {
            build();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException(message);
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
