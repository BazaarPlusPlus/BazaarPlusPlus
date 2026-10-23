#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class EnchantPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Enchant Preview",
        "附魔预览",
        "Verzauberungsvorschau",
        "Previa de Encantamento",
        "마법부여 미리보기",
        "Anteprima Incantamento"
    );

    public static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
