#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterAttributionText
{
    private static readonly LocalizedTextSet SupportedByPrefix = new("Supported by", "由");
    private static readonly LocalizedTextSet SupportedBySuffix = new(string.Empty, "支持");
    private static readonly LocalizedTextSet SponsorAction = new("Sponsor", "赞助");

    public static string FormatSupportedByPrefix(string languageCode)
    {
        return SupportedByPrefix.Resolve(languageCode, L.CurrentMode);
    }

    public static string FormatSupportedBySuffix(string languageCode)
    {
        return SupportedBySuffix.Resolve(languageCode, L.CurrentMode);
    }

    public static string FormatSponsorAction(string languageCode)
    {
        return SponsorAction.Resolve(languageCode, L.CurrentMode);
    }
}
