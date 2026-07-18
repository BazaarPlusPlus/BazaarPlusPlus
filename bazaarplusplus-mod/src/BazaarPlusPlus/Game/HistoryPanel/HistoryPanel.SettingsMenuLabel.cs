#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Game History",
        "对局历史",
        "Spielverlauf",
        "Historico de partidas",
        "게임 전적",
        "Cronologia partite"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
