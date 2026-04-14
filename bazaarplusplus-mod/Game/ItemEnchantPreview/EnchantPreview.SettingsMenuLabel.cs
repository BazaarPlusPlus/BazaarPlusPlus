#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet AlwaysShowLabels = new(
        "Always Show Enchant Preview",
        "始终显示附魔预览",
        "Verzauberungsvorschau immer anzeigen",
        "Sempre mostrar previa de encantamento",
        "마법부여 미리보기 항상 표시",
        "Mostra sempre anteprima incantamento"
    );

    private static readonly LocalizedTextSet HoldToShowLabels = new(
        "Hold To Show Enchant Preview",
        "按住显示附魔预览",
        "Halten fuer Verzauberungsvorschau",
        "Segure para ver previa de encantamento",
        "누르고 있을 때 마법부여 미리보기 표시",
        "Tieni premuto per anteprima incantamento"
    );

    internal static string ResolveAlwaysShow(string languageCode)
    {
        return AlwaysShowLabels.Resolve(languageCode);
    }

    internal static string ResolveHoldToShow(string languageCode)
    {
        return HoldToShowLabels.Resolve(languageCode);
    }
}
