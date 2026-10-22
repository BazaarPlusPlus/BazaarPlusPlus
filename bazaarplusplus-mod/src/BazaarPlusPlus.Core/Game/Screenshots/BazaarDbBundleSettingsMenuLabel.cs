#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Screenshots;

public static class BazaarDbBundleSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "BazaarDB upload",
        "BazaarDB 数据共建",
        "BazaarDB-Upload",
        "Subir a BazaarDB",
        "BazaarDB 업로드",
        "Carica su BazaarDB"
    );

    public static string Resolve(string languageCode) =>
        Labels.Resolve(languageCode, L.CurrentMode);
}
