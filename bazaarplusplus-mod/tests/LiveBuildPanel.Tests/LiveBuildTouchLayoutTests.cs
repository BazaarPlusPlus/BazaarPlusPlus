using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;

namespace LiveBuildPanel.Tests;

/// <summary>
/// The touch geometry the Android host's Live Build panel is built on: physical sizes, the safe
/// area, and a layout that neither overflows nor overlaps at the screens the device runs check.
/// </summary>
internal static class LiveBuildTouchLayoutTests
{
    private const float Reference = 1080f;

    public static void Run()
    {
        MetricsScaleDpByDensityAndCanvasHeight();
        MetricsFallBackOnAnUnknownDensity();
        SafeAreaFlipsToTopDownCanvasUnits();
        ColumnCountStaysWithinBounds();
        TextNeverFallsUnderTheBodyMinimum();
        foreach (var (name, metrics) in Screens())
            LayoutFits(name, metrics);
        CardsFollowTheirSocketsAndSpans();
        InspectScaleKeepsTheCardInsideItsArea();
    }

    private static IEnumerable<(string Name, TouchMetrics Metrics)> Screens()
    {
        yield return ("2400x1080@420", Screen(2400f, 1080f, 420f));
        yield return ("2560x1600@420", Screen(2560f, 1600f, 420f));
        yield return ("1920x1080@420", Screen(1920f, 1080f, 420f));
        yield return ("2400x1080@420 cutout", Screen(2400f, 1080f, 420f, cutoutLeft: 110f));
        yield return ("2340x1080@440", Screen(2340f, 1080f, 440f));
    }

    private static TouchMetrics Screen(
        float width,
        float height,
        float dpi,
        float cutoutLeft = 0f
    ) =>
        TouchMetrics.From(
            width,
            height,
            dpi,
            Reference,
            cutoutLeft,
            0f,
            width - cutoutLeft,
            height
        );

    private static void MetricsScaleDpByDensityAndCanvasHeight()
    {
        var phone = Screen(2400f, 1080f, 420f);
        Near(2400f, phone.CanvasWidth, "A height-matched canvas keeps the aspect.");
        Near(2.625f, phone.UnitsPerDp, "420 dpi at the reference height is 2.625 units per dp.");
        Near(126f, phone.MinTarget, "48dp is 126 units on the 2400x1080 phone.");

        var tablet = Screen(2560f, 1600f, 420f);
        Near(1728f, tablet.CanvasWidth, "1600 pixels scale down to the reference height.");
        Near(2.625f * 1080f / 1600f, tablet.UnitsPerDp, "More pixels per unit shrink dp in units.");
    }

    private static void MetricsFallBackOnAnUnknownDensity()
    {
        var unknown = Screen(2400f, 1080f, 0f);
        Near(TouchMetrics.FallbackDpi / 160f, unknown.UnitsPerDp, "No dpi reads as the fallback.");
        var nan = Screen(2400f, 1080f, float.NaN);
        Near(TouchMetrics.FallbackDpi / 160f, nan.UnitsPerDp, "A NaN dpi reads as the fallback.");
    }

    private static void SafeAreaFlipsToTopDownCanvasUnits()
    {
        // Unity's safe area is bottom-up in pixels: a 100px bottom bar and a 64px top cutout.
        var metrics = TouchMetrics.From(2560f, 1600f, 420f, Reference, 80f, 100f, 2400f, 1436f);
        var scale = Reference / 1600f;
        Near(80f * scale, metrics.SafeArea.Left, "The left inset scales.");
        Near(64f * scale, metrics.SafeArea.Top, "The top inset is what the cutout covers.");
        Near(2400f * scale, metrics.SafeArea.Width, "The width scales.");
        Near(1436f * scale, metrics.SafeArea.Height, "The height scales.");

        var empty = TouchMetrics.From(2400f, 1080f, 420f, Reference, 0f, 0f, 0f, 0f);
        Check.Equal(
            new TouchRect(0f, 0f, 2400f, 1080f),
            empty.SafeArea,
            "No safe area is the whole screen."
        );
        var outside = TouchMetrics.From(2400f, 1080f, 420f, Reference, -50f, -50f, 3000f, 2000f);
        Check.Equal(
            new TouchRect(0f, 0f, 2400f, 1080f),
            outside.SafeArea,
            "A safe area past the screen is clamped to it."
        );
    }

    private static void ColumnCountStaysWithinBounds()
    {
        Check.Equal(2, TouchMetrics.ColumnCount(300f, 110f, 10f, 2), "Two fit and two is the cap.");
        Check.Equal(
            3,
            TouchMetrics.ColumnCount(350f, 110f, 10f, 4),
            "Three cells and two gaps fit."
        );
        Check.Equal(1, TouchMetrics.ColumnCount(50f, 110f, 10f, 4), "At least one column.");
        Check.Equal(4, TouchMetrics.ColumnCount(50f, 0f, 10f, 4), "No minimum takes the cap.");
    }

    private static void TextNeverFallsUnderTheBodyMinimum()
    {
        var metrics = Screen(2400f, 1080f, 420f);
        Near(metrics.Dp(12f), metrics.Sp(8f), "Sp clamps to the body minimum.");
        Near(metrics.Dp(48f), metrics.Target(20f), "A target clamps to 48dp.");
        var layout = LiveBuildTouchLayout.For(metrics);
        foreach (
            var size in new[]
            {
                layout.TitleFont,
                layout.RowTitleFont,
                layout.ButtonFont,
                layout.BodyFont,
                layout.ValueFont,
                layout.TagFont,
                layout.NameFont,
            }
        )
            Check.That(size >= metrics.BodyText - 0.01f, $"Text size {size} is under 12sp.");
    }

    private static void LayoutFits(string name, TouchMetrics metrics)
    {
        var layout = LiveBuildTouchLayout.For(metrics);
        var sheet = layout.Sheet;
        Check.That(metrics.SafeArea.Contains(sheet), $"{name}: the sheet leaves the safe area.");

        var targets = new (string Name, TouchRect Rect)[]
        {
            ("close", layout.Close),
            ("refresh", layout.Refresh),
            ("corpus", layout.Corpus),
            ("previous", layout.Previous),
            ("next", layout.Next),
            ("inspect close", layout.InspectClose),
        };
        foreach (var (target, rect) in targets)
        {
            Check.That(
                rect.Width >= metrics.MinTarget - 0.01f && rect.Height >= metrics.MinTarget - 0.01f,
                $"{name}: {target} {rect} is under 48dp."
            );
            Check.That(sheet.Contains(rect), $"{name}: {target} leaves the sheet.");
        }

        var regions = new (string Name, TouchRect Rect)[]
        {
            ("pinned title", layout.PinnedTitleText),
            ("previous", layout.Previous),
            ("pager", layout.Pager),
            ("next", layout.Next),
            ("pinned board", layout.PinnedBoard),
            ("scroll", layout.Scroll),
            ("title", layout.Title),
            ("close", layout.Close),
            ("refresh", layout.Refresh),
            ("corpus", layout.Corpus),
            ("hero", layout.Hero),
            ("info", layout.Info),
            ("hint", layout.Hint),
        };
        foreach (var (region, rect) in regions)
        {
            Check.That(sheet.Contains(rect), $"{name}: {region} {rect} leaves the sheet.");
            Check.That(rect.Width > 0f && rect.Height > 0f, $"{name}: {region} has no room.");
        }
        for (var left = 0; left < regions.Length; left++)
        for (var right = left + 1; right < regions.Length; right++)
            Check.That(
                !regions[left].Rect.Overlaps(regions[right].Rect),
                $"{name}: {regions[left].Name} overlaps {regions[right].Name}."
            );

        Check.That(
            layout.CarpetWidth <= layout.BoardColumn.Width + 0.01f,
            $"{name}: ten sockets overflow the board column."
        );
        Check.That(
            layout.Sidebar.Width >= metrics.Dp(240f) - 0.01f,
            $"{name}: the sidebar is narrower than 240dp."
        );
        Check.That(
            layout.Scroll.Height >= layout.ScrollRowHeight - 0.01f,
            $"{name}: one list row does not fit the viewport."
        );
        Check.That(
            layout.ScrollContentHeight > layout.Scroll.Height,
            $"{name}: the list should scroll, so the drag has something to move."
        );
        for (var row = 0; row < LiveBuildTouchLayout.ScrollRowCount; row++)
        {
            var title = layout.ScrollRowTitle(row);
            var board = layout.ScrollRowBoard(row);
            Check.That(
                title.Bottom <= board.Top + 0.01f,
                $"{name}: row {row} title overlaps its board."
            );
            Check.That(
                board.Bottom <= layout.ScrollContentHeight + 0.01f,
                $"{name}: row {row} leaves the list content."
            );
            if (row > 0)
                Check.That(
                    layout.ScrollRowBoard(row - 1).Bottom <= title.Top + 0.01f,
                    $"{name}: row {row} overlaps the row above."
                );
        }

        Check.That(
            layout.MetricCellHeight >= metrics.Dp(40f) - 0.01f,
            $"{name}: a metric cell is {layout.MetricCellHeight / metrics.UnitsPerDp}dp high."
        );
        for (var index = 0; index < LiveBuildTouchLayout.MetricCount; index++)
        {
            var cell = layout.MetricCell(index);
            Check.That(layout.Info.Contains(cell), $"{name}: metric {index} leaves the info area.");
            for (var other = index + 1; other < LiveBuildTouchLayout.MetricCount; other++)
                Check.That(
                    !cell.Overlaps(layout.MetricCell(other)),
                    $"{name}: metrics {index} and {other} overlap."
                );
        }

        Check.That(
            sheet.Contains(layout.InspectCardArea) && sheet.Contains(layout.InspectInfo),
            $"{name}: the inspect sheet leaves the sheet."
        );
        Check.That(
            !layout.InspectCardArea.Overlaps(layout.InspectInfo)
                && !layout.InspectName.Overlaps(layout.InspectClose),
            $"{name}: the inspect sheet overlaps itself."
        );
    }

    private static void CardsFollowTheirSocketsAndSpans()
    {
        var layout = LiveBuildTouchLayout.For(Screen(2400f, 1080f, 420f));
        var small = layout.CardRect(0, 1);
        var large = layout.CardRect(7, 3);
        Near(layout.SocketWidth, small.Width, "A small card spans one socket.");
        Near(layout.SocketWidth * 7f, large.Left, "A card starts at its socket.");
        Near(layout.CarpetWidth, large.Right, "A large card at socket 7 ends at the tenth socket.");
        Near(
            layout.CarpetWidth,
            layout.CardRect(9, 3).Right,
            "A span past the end is pulled back."
        );
        Near(layout.CardHeight, small.Height, "Every card has the board's card height.");
        var tag = layout.TagRect(0, 1);
        Check.That(
            tag.Top >= small.Bottom - 0.01f && tag.Width <= small.Width + 0.01f,
            "The tag sits in the band under its card, no wider than it."
        );
        Check.That(
            tag.Bottom <= layout.CardHeight + layout.TagHeight + 0.01f,
            "The tag stays inside the board."
        );
        Check.That(
            layout.CardScale > 0.5f,
            $"Cards on the 2400x1080 phone should be readable, scale {layout.CardScale}."
        );
    }

    private static void InspectScaleKeepsTheCardInsideItsArea()
    {
        foreach (var (name, metrics) in Screens())
        {
            var layout = LiveBuildTouchLayout.For(metrics);
            for (var span = 1; span <= 3; span++)
            {
                var scale = layout.InspectScale(span);
                Check.That(
                    LiveBuildTouchLayout.PrefabSocketWidth * span * scale
                        <= layout.InspectCardArea.Width + 0.01f,
                    $"{name}: a {span}-socket card is wider than the inspect area."
                );
                Check.That(
                    LiveBuildTouchLayout.PrefabHeight
                        * LiveBuildTouchLayout.FrameHeightOverSocket
                        * scale
                        <= layout.InspectCardArea.Height + 0.01f,
                    $"{name}: a {span}-socket card is taller than the inspect area."
                );
                Check.That(
                    scale >= layout.CardScale,
                    $"{name}: inspecting should enlarge a {span}-socket card."
                );
            }
        }
    }

    private static void Near(float expected, float actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.01f)
            throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
    }
}
