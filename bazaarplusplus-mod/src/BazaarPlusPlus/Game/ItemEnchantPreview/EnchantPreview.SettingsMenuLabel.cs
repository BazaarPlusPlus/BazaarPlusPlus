#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Enchant Preview",
        "附魔预览",
        "Verzauberungsvorschau",
        "Previa de Encantamento",
        "마법부여 미리보기",
        "Anteprima Incantamento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
