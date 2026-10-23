#nullable enable
using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The 48dp check for controls on the game's own canvases, such as the settings dialog's rows
/// and the options of its dropdowns.
/// </summary>
public sealed class TouchMetricsTargetTests
{
    [Theory]
    [InlineData(420f, 420f)]
    [InlineData(0f, TouchMetrics.FallbackDpi)]
    [InlineData(-1f, TouchMetrics.FallbackDpi)]
    [InlineData(float.NaN, TouchMetrics.FallbackDpi)]
    [InlineData(float.PositiveInfinity, TouchMetrics.FallbackDpi)]
    public void An_unknown_density_reads_as_the_fallback(float dpi, float expected) =>
        Assert.Equal(expected, TouchMetrics.EffectiveDpi(dpi));

    [Theory]
    // The emulator: 420dpi, so 48dp is 126 pixels.
    [InlineData(0f, 420f, 126f)]
    [InlineData(58f, 420f, 68f)]
    [InlineData(126f, 420f, 0f)]
    [InlineData(200f, 420f, 0f)]
    [InlineData(-10f, 420f, 126f)]
    // No reported density: 400dpi, 120 pixels.
    [InlineData(0f, 0f, 120f)]
    [InlineData(100f, 0f, 20f)]
    public void Missing_pixels_make_up_a_48dp_target(float height, float dpi, float expected) =>
        Assert.Equal(expected, TouchMetrics.MissingTargetPixels(height, dpi), 3);
}
