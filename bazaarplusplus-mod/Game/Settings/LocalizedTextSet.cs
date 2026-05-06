#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct LocalizedTextSet
{
    internal LocalizedTextSet(string english, string chineseMainland)
        : this(english, chineseMainland, null, null, english, english, english, english) { }

    internal LocalizedTextSet(
        string english,
        string chineseMainland,
        string chineseTaiwan,
        string chineseHongKong
    )
        : this(
            english,
            chineseMainland,
            chineseTaiwan,
            chineseHongKong,
            english,
            english,
            english,
            english
        ) { }

    internal LocalizedTextSet(
        string english,
        string chineseMainland,
        string german,
        string portuguese,
        string korean,
        string italian
    )
        : this(english, chineseMainland, null, null, german, portuguese, korean, italian) { }

    internal LocalizedTextSet(
        string english,
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong,
        string german,
        string portuguese,
        string korean,
        string italian
    )
    {
        English = english ?? throw new ArgumentNullException(nameof(english));
        ChineseMainland =
            chineseMainland ?? throw new ArgumentNullException(nameof(chineseMainland));
        ChineseTaiwan = chineseTaiwan;
        ChineseHongKong = chineseHongKong;
        German = german ?? throw new ArgumentNullException(nameof(german));
        Portuguese = portuguese ?? throw new ArgumentNullException(nameof(portuguese));
        Korean = korean ?? throw new ArgumentNullException(nameof(korean));
        Italian = italian ?? throw new ArgumentNullException(nameof(italian));
    }

    internal string English { get; }

    internal string ChineseMainland { get; }

    internal string? ChineseTaiwan { get; }

    internal string? ChineseHongKong { get; }

    internal string German { get; }

    internal string Portuguese { get; }

    internal string Korean { get; }

    internal string Italian { get; }

    internal string Resolve(string languageCode)
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return BppChineseLocalization.ResolveChineseText(
                ChineseMainland,
                ChineseTaiwan,
                ChineseHongKong
            );
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
