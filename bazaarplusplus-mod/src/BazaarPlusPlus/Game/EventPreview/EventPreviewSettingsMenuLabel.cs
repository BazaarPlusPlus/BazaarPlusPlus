#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EventPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Event Preview",
        "事件预览",
        "事件預覽",
        "Ereignisvorschau",
        "Prévia de Eventos",
        "이벤트 미리보기",
        "Anteprima Eventi"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
