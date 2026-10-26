#nullable enable
using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The native shortcut and playback targets on the screens the Android host must fit: the 20:9 emulator
/// with its cutouts, a 16:10 tablet with and without its top inset, a 16:9 phone, and a screen
/// that reports no density.
/// </summary>
public sealed class TouchHudLayoutTests
{
    private const float Reference = 1080f;

    // name: width, height, dpi, and the safe area in Unity's pixels (x from the left, y from the
    // bottom). The device fixtures include: 2400x1080 with a cutout on each side,
    // and 2560x1600 with its top 91 px unsafe.
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
    public void Hud_controls_fit_safe_area_and_keep_full_touch_targets(string name)
    {
        var metrics = Metrics(name);
        var safe = metrics.SafeArea;
        var gear = new TouchRect(safe.Right - 80, safe.Top + 20, 60, 60);
        var shortcuts = TouchHudLayout.ShortcutsBelow(metrics, gear, 2);
        foreach (var target in shortcuts)
        {
            Assert.True(safe.Contains(target));
            Assert.True(target.Height >= metrics.MinTarget && target.Width >= metrics.MinTarget);
            Assert.False(target.Overlaps(gear));
        }
        for (var i = 1; i < shortcuts.Length; i++)
            Assert.False(shortcuts[i].Overlaps(shortcuts[i - 1]));
        var beside = TouchHudLayout.ShortcutsBeside(metrics, gear, 2);
        foreach (var target in beside)
        {
            Assert.True(safe.Contains(target));
            Assert.True(target.Height >= metrics.MinTarget && target.Width >= metrics.MinTarget);
            Assert.True(target.Right <= gear.Left);
            Assert.True(target.Top <= gear.CenterY && target.Bottom >= gear.CenterY);
        }
        Assert.True(beside[1].Right <= beside[0].Left);
        var vault = TouchHudLayout.Vault(metrics);
        var clock = TouchHudLayout.Playback(metrics);
        Assert.True(safe.Contains(clock.Panel));
        Assert.True(clock.Panel.Left >= vault.Right);
        Assert.Equal(Math.Max(vault.Top, safe.Top), clock.Panel.Top, 0.01f);
        Assert.Equal(Math.Min(vault.Bottom, safe.Bottom), clock.Panel.Bottom, 0.01f);
        Assert.True(clock.Panel.Contains(clock.Time) && clock.Panel.Contains(clock.Controls));
        Assert.False(clock.Time.Overlaps(clock.Controls));
        Assert.True(clock.Time.Height > clock.Controls.Height);
        foreach (var target in new[] { clock.Speed, clock.Pause })
        {
            Assert.True(safe.Contains(target));
            Assert.True(target.Left >= vault.Right);
            Assert.True(target.Width >= metrics.MinTarget && target.Height >= metrics.MinTarget);
            Assert.True(target.Top <= clock.Controls.Top && target.Bottom >= clock.Controls.Bottom);
        }
        Assert.False(clock.Speed.Overlaps(clock.Pause));
        Assert.True(clock.Speed.Right <= clock.Pause.Left);
    }
}
