#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The frame every Android touch surface shares (<see cref="TouchChrome"/>) and the Collection
/// panel's touch layout on it, on the screens the host must fit: every close button is the same
/// size in the top-right corner of what it closes, every text is at least 12sp, every target at
/// least 48dp, and nothing leaves the safe area or overlaps.
/// </summary>
public sealed class TouchChromeTests
{
    public static TheoryData<string, float, float, float, float, float> Screens =>
        TouchLauncherLayoutTests.Screens;

    private static TouchMetrics Metrics(
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    ) => TouchMetrics.From(width, height, dpi, 1080f, safeLeft, 0f, safeWidth, 0f);

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_surface_closes_from_the_same_button(
        string name,
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    )
    {
        var metrics = Metrics(width, height, dpi, safeLeft, safeWidth);
        var chrome = TouchChrome.For(metrics);
        var history = HistoryTouchLayout.For(metrics);
        var liveBuild = LiveBuildTouchLayout.For(metrics);
        var collection = CollectionTouchLayout.For(metrics);
        var launcher = TouchLauncherLayout.Compute(metrics, 3);
        var closes = new (string Surface, TouchRect Close)[]
        {
            ("history", history.Close),
            ("history inspection", history.InspectClose),
            ("history skills", history.SkillsClose),
            ("history dialog", history.DialogClose),
            ("live build", liveBuild.Close),
            ("live build inspection", liveBuild.InspectClose),
            ("collection", collection.Close),
            ("collection inspection", collection.Inspect.Close),
            ("touch menu", launcher.Close),
        };
        foreach (var (surface, close) in closes)
        {
            Assert.Equal(chrome.CloseWidth, close.Width, 2);
            Assert.Equal(chrome.Target, close.Height, 2);
            Assert.True(close.Height >= metrics.MinTarget - 0.01f, $"{name}: {surface} is 48dp");
            Assert.True(metrics.SafeArea.Contains(close), $"{name}: {surface} in the safe area");
        }
        // The full-screen panels share one close position.
        Assert.Equal(chrome.Close, history.Close);
        Assert.Equal(chrome.Close, liveBuild.Close);
        Assert.Equal(chrome.Close, collection.Close);
        // So do their inspections.
        Assert.Equal(chrome.Inspect().Close, history.InspectClose);
        Assert.Equal(chrome.Inspect().Close, liveBuild.InspectClose);
        Assert.Equal(chrome.Inspect().Close, collection.Inspect.Close);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_surface_reads_at_12sp_or_more(
        string name,
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    )
    {
        var metrics = Metrics(width, height, dpi, safeLeft, safeWidth);
        var chrome = TouchChrome.For(metrics);
        var history = HistoryTouchLayout.For(metrics);
        var liveBuild = LiveBuildTouchLayout.For(metrics);
        var launcher = TouchLauncherLayout.Compute(metrics, 3);
        var fonts = new (string Text, float Size)[]
        {
            ("chrome title", chrome.TitleFont),
            ("chrome name", chrome.NameFont),
            ("chrome button", chrome.ButtonFont),
            ("chrome body", chrome.BodyFont),
            ("history title", history.TitleFont),
            ("history button", history.ButtonFont),
            ("history row", history.RowTitleFont),
            ("history body", history.BodyFont),
            ("history name", history.NameFont),
            ("live build title", liveBuild.TitleFont),
            ("live build row", liveBuild.RowTitleFont),
            ("live build button", liveBuild.ButtonFont),
            ("live build body", liveBuild.BodyFont),
            ("live build value", liveBuild.ValueFont),
            ("live build tag", liveBuild.TagFont),
            ("live build name", liveBuild.NameFont),
            ("launcher", launcher.LauncherFont),
            ("menu title", launcher.TitleFont),
            ("menu entry", launcher.EntryFont),
        };
        foreach (var (text, size) in fonts)
            Assert.True(size >= metrics.BodyText - 0.01f, $"{name}: {text} is at least 12sp");
        // Titles stand out from buttons, and buttons from body text.
        Assert.True(chrome.TitleFont > chrome.ButtonFont, name);
        Assert.True(chrome.ButtonFont > chrome.BodyFont, name);
        Assert.Equal(chrome.TitleFont, history.TitleFont, 2);
        Assert.Equal(chrome.TitleFont, liveBuild.TitleFont, 2);
        Assert.Equal(chrome.NameFont, history.NameFont, 2);
        Assert.Equal(chrome.NameFont, liveBuild.NameFont, 2);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_collection_panel_fits_the_safe_area_without_overlap(
        string name,
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    )
    {
        var metrics = Metrics(width, height, dpi, safeLeft, safeWidth);
        var layout = CollectionTouchLayout.For(metrics);
        var parts = new (string Part, TouchRect Rect)[]
        {
            ("title", layout.Title),
            ("items tab", layout.ItemsTab),
            ("skills tab", layout.SkillsTab),
            ("search", layout.Search),
            ("close", layout.Close),
            ("rail", layout.Rail),
            ("grid", layout.Grid),
        };
        Assert.True(metrics.SafeArea.Contains(layout.Sheet), $"{name}: sheet in the safe area");
        foreach (var (part, rect) in parts)
        {
            Assert.True(layout.Sheet.Contains(rect), $"{name}: {part} in the sheet");
            Assert.True(rect.Width > 0f && rect.Height > 0f, $"{name}: {part} has room");
        }
        for (var first = 0; first < parts.Length; first++)
        for (var second = first + 1; second < parts.Length; second++)
            Assert.False(
                parts[first].Rect.Overlaps(parts[second].Rect),
                $"{name}: {parts[first].Part} overlaps {parts[second].Part}"
            );

        var minimum = metrics.MinTarget - 0.01f;
        foreach (var target in new[] { layout.ItemsTab, layout.SkillsTab, layout.Search })
            Assert.True(target.Height >= minimum && target.Width >= minimum, name);
        Assert.True(layout.ChipHeight >= minimum, $"{name}: a chip is 48dp");
        // The search keeps room for a query on every screen.
        Assert.True(layout.Search.Width >= metrics.Dp(120f), $"{name}: search is 120dp or wider");
        // The rail holds two-column chips at least 48dp wide.
        var chip = (layout.Rail.Width - layout.Gap * 2f) / 3f;
        Assert.True(chip >= minimum, $"{name}: a rail chip is 48dp wide");

        var inspect = layout.Inspect;
        foreach (
            var (part, rect) in new[]
            {
                ("card", inspect.CardArea),
                ("info", inspect.Info),
                ("name", inspect.Name),
                ("detail", inspect.Detail),
            }
        )
            Assert.True(layout.Sheet.Contains(rect), $"{name}: inspection {part} in the sheet");
        Assert.False(inspect.CardArea.Overlaps(inspect.Info), $"{name}: card beside the info");
        Assert.False(inspect.Name.Overlaps(inspect.Close), $"{name}: name beside close");
        Assert.False(inspect.Detail.Overlaps(inspect.Close), $"{name}: detail under close");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_grid_columns_keep_a_card_readable(
        string name,
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    )
    {
        var metrics = Metrics(width, height, dpi, safeLeft, safeWidth);
        var layout = CollectionTouchLayout.For(metrics);
        var viewport = layout.Grid.Width;
        var available = viewport - 2f * CollectionGridConstants.GridOuterPadding;

        var items = layout.GridColumns(CardType.Item, viewport);
        Assert.InRange(
            items,
            CollectionGridConstants.ItemLargeSpan,
            CollectionGridConstants.ItemColumns
        );
        var itemColumn = (available - CollectionGridConstants.GridGap * (items - 1)) / items;
        Assert.True(
            itemColumn >= metrics.Dp(CollectionTouchLayout.MinItemColumnDp) - 0.01f
                || items == CollectionGridConstants.ItemLargeSpan,
            $"{name}: {items} item columns of {itemColumn / metrics.UnitsPerDp:0}dp"
        );

        var skills = layout.GridColumns(CardType.Skill, viewport);
        Assert.InRange(skills, 1, CollectionGridConstants.SkillColumns);
        var skill = (available - CollectionGridConstants.GridGap * (skills - 1)) / skills;
        Assert.True(
            skill >= metrics.Dp(CollectionTouchLayout.MinSkillDp) - 0.01f || skills == 1,
            $"{name}: {skills} skill columns of {skill / metrics.UnitsPerDp:0}dp"
        );
    }

    [Fact]
    public void The_grid_columns_follow_the_width()
    {
        var metrics = Metrics(2400f, 1080f, 420f, 0f, 0f);
        var layout = CollectionTouchLayout.For(metrics);

        Assert.Equal(CollectionGridConstants.ItemLargeSpan, layout.GridColumns(CardType.Item, 0f));
        Assert.Equal(1, layout.GridColumns(CardType.Skill, 0f));
        Assert.Equal(
            CollectionGridConstants.ItemColumns,
            layout.GridColumns(CardType.Item, 100_000f)
        );
        Assert.Equal(
            CollectionGridConstants.SkillColumns,
            layout.GridColumns(CardType.Skill, 100_000f)
        );
        var previous = 0;
        for (var width = 0f; width <= 4000f; width += 50f)
        {
            var columns = layout.GridColumns(CardType.Item, width);
            Assert.True(columns >= previous, $"{width}: columns never drop as the grid widens");
            previous = columns;
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void An_inspection_chip_shows_its_label_whole(
        string name,
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    )
    {
        var layout = CollectionTouchLayout.For(Metrics(width, height, dpi, safeLeft, safeWidth));
        var detail = layout.InspectDetailWidth;

        Assert.Equal(CollectionTouchLayout.InspectChipColumns, layout.InspectChipColumnsFor(0f));
        Assert.Equal(
            CollectionTouchLayout.InspectChipColumns,
            layout.InspectChipColumnsFor(float.NaN)
        );
        Assert.Equal(1, layout.InspectChipColumnsFor(detail * 2f));
        var previous = CollectionTouchLayout.InspectChipColumns;
        for (var chip = 1f; chip <= detail; chip += detail / 64f)
        {
            var columns = layout.InspectChipColumnsFor(chip);
            Assert.InRange(columns, 1, CollectionTouchLayout.InspectChipColumns);
            Assert.True(columns <= previous, $"{name}: fewer columns as the label widens");
            previous = columns;
            // An inner chip, narrowed by a gap, still holds the label.
            if (columns > 1)
                Assert.True(
                    detail / columns - layout.Gap >= chip - 0.01f,
                    $"{name}: a {chip:0} unit chip in {columns} columns"
                );
        }
    }

    [Theory]
    // A rail of 600 units in six columns with a 20 unit gap: a two-column chip is 180 wide.
    [InlineData(600f, 6, 2, 150f, 2)]
    [InlineData(600f, 6, 2, 180f, 2)]
    // "The Dragon" at body size: three columns (280) hold it.
    [InlineData(600f, 6, 2, 200f, 3)]
    [InlineData(600f, 6, 2, 500f, 6)]
    // Wider than the row: the full row, and it shrinks then ellipsizes.
    [InlineData(600f, 6, 3, 900f, 6)]
    // Unmeasured: the request.
    [InlineData(0f, 6, 2, 900f, 2)]
    [InlineData(600f, 6, 2, float.NaN, 2)]
    // Out-of-range requests clamp.
    [InlineData(600f, 6, 0, 10f, 1)]
    [InlineData(600f, 6, 9, 10f, 6)]
    public void A_chip_widens_until_its_label_fits(
        float row,
        int columns,
        int span,
        float chip,
        int expected
    ) => Assert.Equal(expected, TouchChrome.ChipSpan(row, columns, span, 20f, chip));

    [Fact]
    public void The_inspected_card_fits_its_area()
    {
        var layout = CollectionTouchLayout.For(Metrics(2400f, 1080f, 420f, 0f, 0f));
        var area = layout.Inspect.CardArea;

        foreach (var (w, h) in new[] { (252f, 502f), (755f, 502f), (484f, 502f) })
        {
            var scale = layout.InspectScale(w, h);
            Assert.True(w * scale <= area.Width * TouchInspectLayout.CardFill + 0.01f);
            Assert.True(h * scale <= area.Height * TouchInspectLayout.CardFill + 0.01f);
        }
        Assert.Equal(0.01f, layout.InspectScale(0f, 100f));
    }

    [Theory]
    // The desktop's 46% of a 30px label is 13.8px; 12sp at 420dpi is 31.5px, so the label must
    // render at 105%, capped at 100%.
    [InlineData(46, 30f, 420f, 100)]
    // A 63px label at 420dpi: 12sp is 50% of it.
    [InlineData(46, 63f, 420f, 50)]
    // A large label already reads at the desktop's size.
    [InlineData(46, 200f, 420f, 46)]
    // Unknown font size: the desktop's size.
    [InlineData(46, 0f, 420f, 46)]
    [InlineData(46, float.NaN, 420f, 46)]
    // Unknown density: the fallback's 30px floor; 30/60 is 50%.
    [InlineData(46, 60f, 0f, 50)]
    // Out-of-range percents clamp.
    [InlineData(0, 0f, 420f, 1)]
    [InlineData(150, 0f, 420f, 100)]
    public void Preview_text_is_raised_to_12sp(
        int percent,
        float fontPixels,
        float dpi,
        int expected
    ) => Assert.Equal(expected, TouchMetrics.ReadablePercent(percent, fontPixels, dpi));
}
