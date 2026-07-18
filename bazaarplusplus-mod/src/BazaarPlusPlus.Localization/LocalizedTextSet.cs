#nullable enable
namespace BazaarPlusPlus.Localization;

internal readonly struct LocalizedTextSet
{
    internal LocalizedTextSet(string english, string chineseMainland)
        : this(english, chineseMainland, null, english, english, english, english) { }

    internal LocalizedTextSet(string english, string chineseMainland, string chineseTraditional)
        : this(english, chineseMainland, chineseTraditional, english, english, english, english) { }

    internal LocalizedTextSet(
        string english,
        string chineseMainland,
        string german,
        string portuguese,
        string korean,
        string italian
    )
        : this(english, chineseMainland, null, german, portuguese, korean, italian) { }

    internal LocalizedTextSet(
        string english,
        string chineseMainland,
        string? chineseTraditional,
        string german,
        string portuguese,
        string korean,
        string italian
    )
    {
        English = english ?? throw new ArgumentNullException(nameof(english));
        ChineseMainland =
            chineseMainland ?? throw new ArgumentNullException(nameof(chineseMainland));
        ChineseTraditional = chineseTraditional;
        German = german ?? throw new ArgumentNullException(nameof(german));
        Portuguese = portuguese ?? throw new ArgumentNullException(nameof(portuguese));
        Korean = korean ?? throw new ArgumentNullException(nameof(korean));
        Italian = italian ?? throw new ArgumentNullException(nameof(italian));
    }

    internal string English { get; }

    internal string ChineseMainland { get; }

    internal string? ChineseTraditional { get; }

    internal string German { get; }

    internal string Portuguese { get; }

    internal string Korean { get; }

    internal string Italian { get; }

    internal string Resolve(string languageCode, BppChineseLocaleMode mode)
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return ChineseScriptConverter.Convert(ChineseMainland, ChineseTraditional, mode);
        }

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
