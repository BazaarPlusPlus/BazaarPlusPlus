#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.ChineseTranslations;

public static class ChineseTranslationOverrides
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Overrides = new(Load);

    // The game's Simplified Chinese locale is zh-CN. Do not use IsChinese: it includes
    // Traditional Chinese and would apply Simplified replacements to those locales too.
    public static bool AppliesTo(string languageCode) =>
        string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);

    public static bool TryResolve(
        bool enabled,
        string languageCode,
        string? key,
        out string translation
    )
    {
        translation = string.Empty;
        return enabled
            && AppliesTo(languageCode)
            && key != null
            && Overrides.Value.TryGetValue(key, out translation!);
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            using var stream =
                typeof(ChineseTranslationOverrides).Assembly.GetManifestResourceStream(
                    "BazaarPlusPlus.Core.Game.ChineseTranslations.zh-CN.overrides.json"
                )
                ?? throw new InvalidOperationException(
                    "Embedded Chinese translation overrides missing."
                );
            using var reader = new StreamReader(stream);
            using var json = new JsonTextReader(reader);
            return new JsonSerializer().Deserialize<Dictionary<string, string>>(json)
                ?? throw new InvalidOperationException(
                    "Embedded Chinese translation overrides empty."
                );
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.chinese_translations.load_failed"
                ),
                exception
            );
            return new Dictionary<string, string>();
        }
    }
}
