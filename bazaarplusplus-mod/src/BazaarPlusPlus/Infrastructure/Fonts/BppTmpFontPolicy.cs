#nullable enable
namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class BppTmpFontPolicy
{
    public static bool ShouldUseEmbeddedCjkFont(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var character in text)
        {
            if (IsCoveredByEmbeddedCjkFont(character))
                return true;
        }

        return false;
    }

    private static bool IsCoveredByEmbeddedCjkFont(char character)
    {
        return character >= '\u4E00' && character <= '\u9FFF';
    }
}
