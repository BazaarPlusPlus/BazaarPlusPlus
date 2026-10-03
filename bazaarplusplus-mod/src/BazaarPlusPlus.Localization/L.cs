#nullable enable
namespace BazaarPlusPlus.Localization;

internal static class L
{
    private static Func<string>? _language;
    private static Func<BppChineseLocaleMode>? _mode;

    internal static void Install(Func<string> language, Func<BppChineseLocaleMode> mode)
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
        return set.Resolve(Language(), Mode());
    }

    // Simplified text a feature shows as Chinese without a LocalizedTextSet (a Chinese-only
    // subtitle row, a formatter's Chinese branch) still follows the Chinese locale mode.
    internal static string ResolveChinese(string simplified) =>
        ChineseScriptConverter.Convert(simplified, null, Mode());

    internal static string CurrentLanguageCode => Language();

    internal static BppChineseLocaleMode CurrentMode => Mode();

    private static Func<string> Language =>
        _language ?? throw new InvalidOperationException("L.Install must be called at startup.");

    private static Func<BppChineseLocaleMode> Mode =>
        _mode ?? throw new InvalidOperationException("L.Install must be called at startup.");
}
