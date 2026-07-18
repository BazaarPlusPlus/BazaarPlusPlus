#nullable enable
namespace BazaarPlusPlus.GameInterop.Fonts;

internal static class UnicodeFontCoverage
{
    internal static bool ContainsCjk(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var character in text)
            if (
                (character >= '\u3400' && character <= '\u9FFF')
                || (character >= '\uF900' && character <= '\uFAFF')
            )
                return true;

        return false;
    }

    internal static bool TryFindMissingCodePoint(
        string? text,
        Func<char, bool> hasCharacter,
        out int missingCodePoint
    )
    {
        if (hasCharacter == null)
            throw new ArgumentNullException(nameof(hasCharacter));

        missingCodePoint = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character))
            {
                missingCodePoint =
                    index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                        ? char.ConvertToUtf32(character, text[index + 1])
                        : character;
                return true;
            }

            if (char.IsLowSurrogate(character) || !hasCharacter(character))
            {
                missingCodePoint = character;
                return true;
            }
        }

        return false;
    }
}
