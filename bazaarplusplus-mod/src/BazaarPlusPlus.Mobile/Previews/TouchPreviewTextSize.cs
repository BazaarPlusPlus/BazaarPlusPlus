using System.Globalization;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Mobile.Previews;

internal static class TouchPreviewTextSize
{
    public const int DefaultPercent = 75;
    private const int MinPercent = 50;
    private const int MaxPercent = 150;

    public static readonly LocalizedTextSet Label = new(
        "Preview text size",
        "预览字号",
        "預覽字號"
    );

    private static readonly int[] Ladder = { 75, 90, 100 };

    public static ISetting<int> Bind(IBppSettingsStore store) =>
        (store ?? throw new ArgumentNullException(nameof(store))).Bind(
            "Touch",
            "PreviewTextSize",
            DefaultPercent,
            "Text size of the Detailed Preview inside a native tooltip, as a percent of the tooltip's own text.",
            new SettingRange<int>(MinPercent, MaxPercent)
        );

    public static string Wrap(string content, int percent) =>
        "<size="
        + Math.Clamp(percent, MinPercent, MaxPercent).ToString(CultureInfo.InvariantCulture)
        + "%>"
        + content
        + "</size>";

    public static CyclingSettingsDockEntry<int> DockEntry(
        ISetting<int> setting,
        Action<int>? onChanged = null
    )
    {
        if (setting == null)
            throw new ArgumentNullException(nameof(setting));
        return new CyclingSettingsDockEntry<int>(
            BppSettingsDockOrder.PreviewTextSize,
            "PreviewTextSize",
            languageCode => Label.Resolve(languageCode, L.CurrentMode),
            Ladder,
            _ => setting.Value,
            (_, percent) => setting.Value = percent,
            percent => percent != DefaultPercent,
            (percent, _) => percent.ToString(CultureInfo.InvariantCulture) + "%",
            onChanged
        );
    }
}
