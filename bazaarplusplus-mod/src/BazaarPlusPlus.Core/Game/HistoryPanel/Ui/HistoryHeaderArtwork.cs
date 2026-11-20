#nullable enable
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

public static class HistoryHeaderArtwork
{
    public const string Normal = "control-normal.png";
    public const string Selected = "control-selected.png";
    public const float BorderPixels = 40f;
    public const float PixelsPerUnit = 100f;
    public const float ButtonBorder = 6f;

    /// <summary>Keep sliced frame borders at ButtonBorder dp across touch screen densities.</summary>
    public static float TouchMultiplier(TouchMetrics metrics) =>
        BorderPixels / metrics.Dp(ButtonBorder);
}
