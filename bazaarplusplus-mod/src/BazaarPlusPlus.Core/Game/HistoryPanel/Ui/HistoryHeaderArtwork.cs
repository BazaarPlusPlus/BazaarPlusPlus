#nullable enable
using BazaarPlusPlus.Core.Ui;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

/// <summary>The two hosts import the same History header PNGs with the same sliced corners.</summary>
public static class HistoryHeaderArtwork
{
    public const string Normal = "control-normal.png";
    public const string Selected = "control-selected.png";
    public const float BorderPixels = 40f;
    public const float PixelsPerUnit = 100f;
    public const float ButtonBorder = 6f;

    /// <summary>
    /// The sliced image's pixels-per-unit multiplier on a touch surface: the frame keeps
    /// <see cref="ButtonBorder"/> dp on every screen density, for History and Ten-Win Builds alike.
    /// </summary>
    public static float TouchMultiplier(TouchMetrics metrics) =>
        BorderPixels / metrics.Dp(ButtonBorder);
}
