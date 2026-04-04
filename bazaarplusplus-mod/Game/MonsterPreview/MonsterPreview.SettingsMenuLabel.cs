#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Use Native Monster Preview",
        "使用原生野怪预览",
        "Native Monstervorschau verwenden",
        "Usar previa nativa de monstro",
        "기본 몬스터 미리보기 사용",
        "Usa anteprima mostro nativa"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
