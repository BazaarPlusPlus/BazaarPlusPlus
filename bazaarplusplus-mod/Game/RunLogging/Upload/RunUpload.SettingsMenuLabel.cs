#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal static class RunUploadSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Community Contribution",
        "参与社区数据共建",
        "Community-Daten beitragen",
        "Contribuir com dados da comunidade",
        "커뮤니티 데이터 공유 참여",
        "Contribuisci ai dati della community"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
