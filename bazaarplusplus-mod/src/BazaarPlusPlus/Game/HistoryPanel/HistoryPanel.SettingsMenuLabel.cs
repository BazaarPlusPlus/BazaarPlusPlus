#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelSettingsMenuLabel
{
    private static readonly LocalizedTextSet LabelFormats = new(
        "Game History (Press {0} to open)",
        "对局历史（按 {0} 打开）",
        "Spielverlauf ({0} zum Öffnen drücken)",
        "Histórico de partidas (pressione {0} para abrir)",
        "게임 전적({0} 키로 열기)",
        "Cronologia partite (premi {0} per aprire)"
    );

    internal static string Resolve(string languageCode, string hotkeyDisplay)
    {
        return string.Format(LabelFormats.Resolve(languageCode, L.CurrentMode), hotkeyDisplay);
    }
}
