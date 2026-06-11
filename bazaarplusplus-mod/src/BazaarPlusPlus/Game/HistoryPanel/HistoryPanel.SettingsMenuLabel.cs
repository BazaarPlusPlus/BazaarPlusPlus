#nullable enable
using BazaarPlusPlus.Game.Settings;
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

    private static readonly LocalizedTextSet OpenStatuses = new(
        "OPEN",
        "已打开",
        "OFFEN",
        "ABERTO",
        "열림",
        "APERTO"
    );

    private static readonly LocalizedTextSet ViewStatuses = new(
        "VIEW",
        "查看",
        "ANZEIGEN",
        "VER",
        "보기",
        "VEDI"
    );

    private static readonly LocalizedTextSet InRunStatuses = new(
        "UNAVAILABLE",
        "战斗中不可用",
        "VORUBERGEHEND NICHT VERFUGBAR",
        "INDISPONIVEL",
        "대국 중 일시 사용 불가",
        "TEMPORANEAMENTE NON DISPONIBILE"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveOpenStatus(string languageCode)
    {
        return OpenStatuses.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveViewStatus(string languageCode)
    {
        return ViewStatuses.Resolve(languageCode, L.CurrentMode);
    }

    internal static string ResolveInRunStatus(string languageCode)
    {
        return InRunStatuses.Resolve(languageCode, L.CurrentMode);
    }
}
