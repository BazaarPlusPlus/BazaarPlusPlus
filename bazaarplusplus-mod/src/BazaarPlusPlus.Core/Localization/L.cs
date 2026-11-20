#nullable enable
namespace BazaarPlusPlus.Localization;

public static class L
{
    private static Func<string>? _language;
    private static Func<BppChineseLocaleMode>? _mode;

    public static void Install(Func<string> language, Func<BppChineseLocaleMode> mode)
    {
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    }

    public static void Reset()
    {
        _language = null;
        _mode = null;
    }

    public static string Resolve(LocalizedTextSet set)
    {
        return set.Resolve(Language(), Mode());
    }

    // Chinese-only strings still honor the selected script mode even without a LocalizedTextSet.
    public static string ResolveChinese(string simplified) =>
        ChineseScriptConverter.Convert(simplified, null, Mode());

    public static string CurrentLanguageCode => Language();

    public static BppChineseLocaleMode CurrentMode => Mode();

    private static Func<string> Language =>
        _language ?? throw new InvalidOperationException("L.Install must be called at startup.");

    private static Func<BppChineseLocaleMode> Mode =>
        _mode ?? throw new InvalidOperationException("L.Install must be called at startup.");
}
