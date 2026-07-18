#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.QuestPreview;

internal static class QuestPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Quest Preview",
        "任务预览",
        "任務預覽",
        "Questvorschau",
        "Prévia de Missão",
        "퀘스트 미리보기",
        "Anteprima Missione"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
