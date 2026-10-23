#nullable enable
using System.Globalization;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public static class HistoryPanelSettingsMenuLabel
{
    private static readonly LocalizedTextSet LabelFormats = new(
        "Game History (Press {0} to open)",
        "对局历史（按 {0} 打开）",
        "Spielverlauf ({0} zum Öffnen drücken)",
        "Histórico de partidas (pressione {0} para abrir)",
        "게임 전적({0} 키로 열기)",
        "Cronologia partite (premi {0} per aprire)"
    );

    private static readonly LocalizedTextSet PlainLabels = new(
        "Game History",
        "对局历史",
        "Spielverlauf",
        "Histórico de partidas",
        "게임 전적",
        "Cronologia partite"
    );

    /// <summary>The label of a host that opens History without a hotkey.</summary>
    public static string ResolvePlain(string languageCode) =>
        PlainLabels.Resolve(languageCode, L.CurrentMode);

    public static string Resolve(string languageCode, string hotkeyDisplay)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            LabelFormats.Resolve(languageCode, L.CurrentMode),
            hotkeyDisplay
        );
    }
}
