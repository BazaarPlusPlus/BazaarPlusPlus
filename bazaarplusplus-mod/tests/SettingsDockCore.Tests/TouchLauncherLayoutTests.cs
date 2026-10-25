#nullable enable
using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The touch launcher and its menu on the screens the Android host must fit: the 20:9 emulator
/// with its cutouts, a 16:10 tablet with and without its top inset, a 16:9 phone, and a screen
/// that reports no density.
/// </summary>
public sealed class TouchLauncherLayoutTests
{
    private const float Reference = 1080f;

    // name: width, height, dpi, and the safe area in Unity's pixels (x from the left, y from the
    // bottom). The two device screens are the emulator's: 2400x1080 with a cutout on each side,
    // and 2560x1600 (`wm size`) with its top 91 px unsafe.
    private static readonly Dictionary<
        string,
        (float Width, float Height, float Dpi, float X, float Y, float SafeWidth, float SafeHeight)
    > Devices = new()
    {
        ["2400x1080@420 cutouts"] = (2400f, 1080f, 420f, 136f, 0f, 2128f, 1080f),
        ["2400x1080@420"] = (2400f, 1080f, 420f, 0f, 0f, 2400f, 1080f),
        ["2560x1600@420 top inset"] = (2560f, 1600f, 420f, 0f, 0f, 2560f, 1509f),
        ["2560x1600@420"] = (2560f, 1600f, 420f, 0f, 0f, 2560f, 1600f),
        ["1920x1080@420"] = (1920f, 1080f, 420f, 0f, 0f, 1920f, 1080f),
        ["2400x1080 unknown dpi"] = (2400f, 1080f, 0f, 0f, 0f, 2400f, 1080f),
    };

    public static TheoryData<string> Screens => new(Devices.Keys);

    /// <summary>The overlay canvas's metrics on the named screen.</summary>
    public static TouchMetrics Metrics(string name)
    {
        var device = Devices[name];
        return TouchMetrics.From(
            device.Width,
            device.Height,
            device.Dpi,
            Reference,
            device.X,
            device.Y,
            device.SafeWidth,
            device.SafeHeight
        );
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_target_fits_the_safe_area_without_overlap(string name)
    {
        var metrics = Metrics(name);
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

    /// <summary>
    /// The game's own controls near the left edge, as shares of the screen height (the game
    /// scales its HUD by height, so the shares hold at every ratio), measured on the emulator at
    /// 2400x1080 and 2560x1600 (PR #454's device evidence, the in-run launcher shots and
    /// the main menu): the main menu's account crest, the in-run day and hour dial, and the main
    /// menu's MENU button, each across the left edge up to the board. The in-run board itself
    /// (cards, hero portrait) starts 0.587 screen heights left of the screen's center and runs
    /// from 46% of the height down.
    /// </summary>
    private static IEnumerable<(string Control, TouchRect Rect)> NativeLeftEdge(
        TouchMetrics metrics
    )
    {
        var height = metrics.CanvasHeight;
        var boardLeft = metrics.CanvasWidth / 2f - 0.587f * height;
        TouchRect Band(float top, float bottom) =>
            new(0f, top * height, boardLeft, (bottom - top) * height);
        yield return ("account crest", Band(0f, 0.17f));
        yield return ("day and hour dial", Band(0.34f, 0.63f));
        yield return ("MENU button", Band(0.84f, 0.94f));
        yield return (
            "board",
            new TouchRect(boardLeft, 0.46f * height, metrics.CanvasWidth, 0.54f * height)
        );
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_launcher_clears_the_game_s_left_edge_controls(string name)
    {
        var metrics = Metrics(name);
        var launcher = TouchLauncherLayout.Compute(metrics, 3).Launcher;

        foreach (var (control, rect) in NativeLeftEdge(metrics))
            Assert.False(launcher.Overlaps(rect), $"{name}: the launcher covers the {control}");
    }

    [Fact]
    public void The_launcher_keeps_to_the_safe_area_left_edge()
    {
        var full = Metrics("2400x1080@420");
        var cutout = Metrics("2400x1080@420 cutouts");

        var shift =
            TouchLauncherLayout.Compute(cutout, 3).Launcher.Left
            - TouchLauncherLayout.Compute(full, 3).Launcher.Left;

        // 136 px of cutout at 1 canvas unit per pixel.
        Assert.Equal(136f, shift, 2);
    }

    [Fact]
    public void The_launcher_stays_inside_a_short_safe_area()
    {
        // A safe area that ends above the launcher's band: the button rises to stay inside it.
        var metrics = TouchMetrics.From(2400f, 1080f, 420f, Reference, 0f, 540f, 2400f, 540f);
        var launcher = TouchLauncherLayout.Compute(metrics, 3).Launcher;

        Assert.True(metrics.SafeArea.Contains(launcher), "launcher in the safe area");
        Assert.Equal(metrics.SafeArea.Bottom - TouchChrome.For(metrics).Margin, launcher.Bottom, 2);
    }

    [Fact]
    public void A_negative_entry_count_is_refused()
    {
        var metrics = Metrics("2400x1080@420");
        Assert.Throws<ArgumentOutOfRangeException>(() => TouchLauncherLayout.Compute(metrics, -1));
    }
}
