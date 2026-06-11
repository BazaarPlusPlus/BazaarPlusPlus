#nullable enable
using System;

namespace BazaarPlusPlus.Localization;

internal static class L
{
    private static ILanguageProvider? _language;
    private static ILocaleModeProvider? _mode;

    internal static void Install(ILanguageProvider language, ILocaleModeProvider mode)
    {
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    }

    internal static void Reset()
    {
        _language = null;
        _mode = null;
    }

    internal static string Resolve(LocalizedTextSet set)
    {
        return set.Resolve(Language.CurrentLanguageCode, Mode.CurrentMode);
    }

    internal static string CurrentLanguageCode => Language.CurrentLanguageCode;

    internal static BppChineseLocaleMode CurrentMode => Mode.CurrentMode;

    private static ILanguageProvider Language =>
        _language ?? throw new InvalidOperationException("L.Install must be called at startup.");

    private static ILocaleModeProvider Mode =>
        _mode ?? throw new InvalidOperationException("L.Install must be called at startup.");
}
