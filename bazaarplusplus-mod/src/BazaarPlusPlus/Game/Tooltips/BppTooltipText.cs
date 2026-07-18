#nullable enable
using TheBazaar;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class BppTooltipText
{
    internal static string ColorKeywords(string text)
    {
        try
        {
            return Data.TooltipTypography?.ColorKeywords(text) ?? text;
        }
        catch (Exception)
        {
            return text;
        }
    }

    internal static string? TryLocalizeKeyword(string canonicalName)
    {
        try
        {
            var languageCode = PlayerPreferences.Data.LanguageCode ?? string.Empty;
            if (
                languageCode.Length == 0
                || languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            )
                return null;

            var typography = Data.TooltipTypography;
            if (typography == null)
                return null;

            string? echo = null;
            foreach (
                var translation in typography.GetKeywordTranslations(
                    $"{{keyword.{canonicalName.ToLowerInvariant()}}}",
                    includePrimaryTranslation: true
                )
            )
            {
                if (string.IsNullOrWhiteSpace(translation))
                    continue;
                if (string.Equals(translation, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    echo ??= translation;
                    continue;
                }
                return translation;
            }
            return echo;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
