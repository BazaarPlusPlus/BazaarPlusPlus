#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal static class BazaarDbSnapshotUploadSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "BazaarDB upload",
        "BazaarDB 数据共建",
        "BazaarDB-Upload",
        "Subir a BazaarDB",
        "BazaarDB 업로드",
        "Carica su BazaarDB"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
