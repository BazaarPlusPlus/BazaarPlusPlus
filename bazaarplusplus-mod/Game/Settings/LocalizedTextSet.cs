using System;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct LocalizedTextSet
{
    internal LocalizedTextSet(
        string english,
        string simplifiedChinese,
        string german,
        string portuguese,
        string korean,
        string italian
    )
    {
        English = english ?? throw new ArgumentNullException(nameof(english));
        SimplifiedChinese =
            simplifiedChinese ?? throw new ArgumentNullException(nameof(simplifiedChinese));
        German = german ?? throw new ArgumentNullException(nameof(german));
        Portuguese = portuguese ?? throw new ArgumentNullException(nameof(portuguese));
        Korean = korean ?? throw new ArgumentNullException(nameof(korean));
        Italian = italian ?? throw new ArgumentNullException(nameof(italian));
    }

    internal string English { get; }

    internal string SimplifiedChinese { get; }

    internal string German { get; }

    internal string Portuguese { get; }

    internal string Korean { get; }

    internal string Italian { get; }

    internal string Resolve(string languageCode)
    {
        if (LanguageCodeMatcher.IsSimplifiedChinese(languageCode))
            return SimplifiedChinese;
        if (LanguageCodeMatcher.IsGerman(languageCode))
            return German;
        if (LanguageCodeMatcher.IsPortuguese(languageCode))
            return Portuguese;
        if (LanguageCodeMatcher.IsKorean(languageCode))
            return Korean;
        if (LanguageCodeMatcher.IsItalian(languageCode))
            return Italian;

        return English;
    }
}
