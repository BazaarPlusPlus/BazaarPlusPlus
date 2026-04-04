#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Always Show Enchant Preview",
        "始终显示附魔预览",
        "Verzauberungsvorschau immer anzeigen",
        "Sempre mostrar previa de encantamento",
        "마법부여 미리보기 항상 표시",
        "Mostra sempre anteprima incantamento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
