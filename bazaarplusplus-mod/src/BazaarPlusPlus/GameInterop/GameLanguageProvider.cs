#nullable enable
using BazaarPlusPlus.Localization;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop;

internal sealed class GameLanguageProvider : ILanguageProvider
{
    public string CurrentLanguageCode
    {
        get
        {
            try
            {
                return PlayerPreferences.Data.LanguageCode ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
