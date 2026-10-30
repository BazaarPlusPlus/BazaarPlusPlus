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

    /// <summary>
    /// Where the bank sits on a height-scaled, centered board: its right border 0.59 canvas
    /// heights right of center, spanning 0.761 to 0.978 canvas heights from the top.
    /// </summary>
    private static TouchRect Bank(TouchMetrics metrics)
    {
        var height = metrics.CanvasHeight;
        return new(
            metrics.CanvasWidth / 2 + height * .378f,
            height * .761f,
            height * .212f,
            height * .217f
        );
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Hud_controls_fit_safe_area_and_keep_full_touch_targets(string name)
    {
        var metrics = Metrics(name);
        var safe = metrics.SafeArea;
        var gear = new TouchRect(safe.Right - 80, safe.Top + 20, 60, 60);
        var below = TouchHudLayout.ShortcutBelow(metrics, gear);
        var beside = TouchHudLayout.ShortcutBeside(metrics, gear);
        foreach (var target in new[] { below, beside })
        {
            Assert.True(safe.Contains(target));
            Assert.True(target.Height >= metrics.MinTarget && target.Width >= metrics.MinTarget);
            Assert.False(target.Overlaps(gear));
        }
        Assert.False(below.Overlaps(beside));
        Assert.True(below.Top >= gear.Bottom);
        Assert.True(beside.Right <= gear.Left);
        Assert.True(beside.Top <= gear.CenterY && beside.Bottom >= gear.CenterY);
        var vault = Bank(metrics);
        Assert.True(TouchHudLayout.FitsBesideVault(metrics, vault));
        var clock = TouchHudLayout.Playback(metrics, vault);
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

    [Theory]
    [MemberData(nameof(Screens))]
    public void Clock_follows_the_vault_wherever_the_board_puts_it(string name)
    {
        var metrics = Metrics(name);
        var bank = Bank(metrics);
        // A cosmetic narrower than the usual bank and sitting higher on the board.
        var vault = new TouchRect(bank.Left + 30, bank.Top - 40, bank.Width - 60, bank.Height - 20);
        Assert.True(TouchHudLayout.FitsBesideVault(metrics, vault));
        var clock = TouchHudLayout.Playback(metrics, vault);
        Assert.True(metrics.SafeArea.Contains(clock.Panel));
        Assert.Equal(vault.Right + metrics.Dp(4), clock.Panel.Left, 0.01f);
        Assert.Equal(Math.Max(vault.Top, metrics.SafeArea.Top), clock.Panel.Top, 0.01f);
        Assert.Equal(Math.Min(vault.Bottom, metrics.SafeArea.Bottom), clock.Panel.Bottom, 0.01f);
    }

    /// <summary>
    /// A foldable's inner display (2480x2248) fills with the board scaled by width, which leaves
    /// a band under it; the vault read there on the emulator sits 0.15 canvas heights above the
    /// bottom edge.
    /// </summary>
    [Fact]
    public void Clock_sits_under_the_vault_on_a_nearly_square_screen()
    {
        var metrics = TouchMetrics.From(2480f, 2248f, 420f, Reference, 0f, 0f, 2480f, 2248f);
        var safe = metrics.SafeArea;
        var vault = new TouchRect(875f, 735f, 163f, 163f);
        Assert.True(TouchHudLayout.FitsBesideVault(metrics, vault));
        var clock = TouchHudLayout.Playback(metrics, vault);
        Assert.True(safe.Contains(clock.Panel));
        Assert.False(clock.Panel.Overlaps(vault));
        Assert.True(clock.Panel.Top >= vault.Bottom);
        Assert.Equal(vault.CenterX, clock.Panel.CenterX, 0.01f);
        Assert.Equal(metrics.Dp(TouchHudLayout.BelowVaultHeightDp), clock.Panel.Height, 0.01f);
        Assert.True(clock.Panel.Contains(clock.Time) && clock.Panel.Contains(clock.Controls));
        foreach (var target in new[] { clock.Speed, clock.Pause })
        {
            Assert.True(safe.Contains(target));
            Assert.True(target.Width >= metrics.MinTarget && target.Height >= metrics.MinTarget);
            Assert.True(target.Top <= clock.Controls.Top && target.Bottom >= clock.Controls.Bottom);
        }
        Assert.Equal(clock.Speed.Right, clock.Pause.Left, 0.01f);
        Assert.Equal(clock.Controls.CenterX, clock.Speed.Right, 0.01f);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_clock_beside_a_vault_read_it_cannot_sit_beside(string name)
    {
        var metrics = Metrics(name);
        var bank = Bank(metrics);
        foreach (
            var read in new[]
            {
                // Off the canvas, left of center (the stash), shorter than a touch target, not finite.
                bank with
                {
                    Top = metrics.CanvasHeight - 10,
                },
                bank with
                {
                    Left = 10,
                },
                bank with
                {
                    Height = metrics.MinTarget / 2,
                },
                bank with
                {
                    Width = float.NaN,
                },
            }
        )
            Assert.False(TouchHudLayout.FitsBesideVault(metrics, read));
    }
}
