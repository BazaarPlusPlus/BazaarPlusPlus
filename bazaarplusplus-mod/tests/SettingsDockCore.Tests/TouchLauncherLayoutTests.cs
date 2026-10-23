#nullable enable
using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The touch launcher and its menu on the screens the Android host must fit: the 20:9 emulator
/// with its cutout, a 16:10 tablet, a 16:9 phone, and a screen that reports no density.
/// </summary>
public sealed class TouchLauncherLayoutTests
{
    public static TheoryData<string, float, float, float, float, float> Screens =>
        new()
        {
            // name, width, height, dpi, safe left, safe width (0: the whole screen)
            { "2400x1080@420 cutouts", 2400f, 1080f, 420f, 136f, 2128f },
            { "2400x1080@420", 2400f, 1080f, 420f, 0f, 0f },
            { "2560x1600@420", 2560f, 1600f, 420f, 0f, 0f },
            { "1920x1080@420", 1920f, 1080f, 420f, 0f, 0f },
            { "2400x1080 unknown dpi", 2400f, 1080f, 0f, 0f, 0f },
        };

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_target_fits_the_safe_area_without_overlap(
        string name,
        float width,
        float height,
        float dpi,
        float safeLeft,
        float safeWidth
    )
    {
        var metrics = TouchMetrics.From(width, height, dpi, 1080f, safeLeft, 0f, safeWidth, 0f);
        for (var entries = 0; entries <= 3; entries++)
        {
            var layout = TouchLauncherLayout.Compute(metrics, entries);
            var label = $"{name}, {entries} entries";

            Assert.True(
                metrics.SafeArea.Contains(layout.Launcher),
                $"{label}: launcher in safe area"
            );
            Assert.True(metrics.SafeArea.Contains(layout.Menu), $"{label}: menu in safe area");
            Assert.Equal(Math.Max(1, entries), layout.Entries.Count);

            var targets = new List<(string Name, TouchRect Rect)>
            {
                ("title", layout.Title),
                ("close", layout.Close),
            };
            targets.AddRange(layout.Entries.Select((rect, index) => ($"entry {index}", rect)));
            foreach (var (part, rect) in targets)
                Assert.True(layout.Menu.Contains(rect), $"{label}: {part} in the menu");
            for (var first = 0; first < targets.Count; first++)
            for (var second = first + 1; second < targets.Count; second++)
                Assert.False(
                    targets[first].Rect.Overlaps(targets[second].Rect),
                    $"{label}: {targets[first].Name} overlaps {targets[second].Name}"
                );

            var minimum = metrics.MinTarget - 0.01f;
            Assert.True(layout.Launcher.Height >= minimum, $"{label}: launcher is 48dp");
            Assert.True(layout.Launcher.Width >= minimum, $"{label}: launcher is 48dp wide");
            Assert.True(layout.Close.Height >= minimum && layout.Close.Width >= minimum, label);
            Assert.All(layout.Entries, entry => Assert.True(entry.Height >= minimum, label));
            foreach (var font in new[] { layout.LauncherFont, layout.TitleFont, layout.EntryFont })
                Assert.True(font >= metrics.BodyText - 0.01f, $"{label}: text is at least 12sp");
        }
    }

    [Fact]
    public void The_launcher_keeps_to_the_safe_area_left_edge()
    {
        var full = TouchMetrics.From(2400f, 1080f, 420f, 1080f, 0f, 0f, 0f, 0f);
        var cutout = TouchMetrics.From(2400f, 1080f, 420f, 1080f, 136f, 0f, 2128f, 0f);

        var shift =
            TouchLauncherLayout.Compute(cutout, 3).Launcher.Left
            - TouchLauncherLayout.Compute(full, 3).Launcher.Left;

        Assert.Equal(cutout.SafeArea.Left - full.SafeArea.Left, shift, 2);
    }

    [Fact]
    public void A_negative_entry_count_is_refused()
    {
        var metrics = TouchMetrics.From(2400f, 1080f, 420f, 1080f, 0f, 0f, 0f, 0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => TouchLauncherLayout.Compute(metrics, -1));
    }
}
