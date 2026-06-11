#nullable enable

namespace BazaarPlusPlus.Localization;

internal static class CjkDetection
{
    internal static bool ContainsCjk(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var character in text)
        {
            if (IsCjk(character))
                return true;
        }

        return false;
    }

    private static bool IsCjk(char character)
    {
        return character
            is >= '\u3400'
                and <= '\u4DBF'
                or >= '\u4E00'
                and <= '\u9FFF'
                or >= '\uF900'
                and <= '\uFAFF';
    }
}
