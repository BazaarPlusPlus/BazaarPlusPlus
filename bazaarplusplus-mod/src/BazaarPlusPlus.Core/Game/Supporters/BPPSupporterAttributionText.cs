#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Supporters;

public static class BPPSupporterAttributionText
{
    private static readonly LocalizedTextSet SupportedByPrefix = new("Supported by", "由");
    private static readonly LocalizedTextSet SupportedBySuffix = new(string.Empty, "支持");
    private static readonly LocalizedTextSet SponsorAction = new("Sponsor", "赞助");
    private static readonly LocalizedTextSet Thanks = new("Thanks", "感谢", "感謝");
    private static readonly LocalizedTextSet Community = new("the community", "社区", "社群");

    public static string FormatSupportedByPrefix(string languageCode)
    {
        return SupportedByPrefix.Resolve(languageCode, L.CurrentMode);
    }

    public static string FormatSupportedBySuffix(string languageCode)
    {
        return SupportedBySuffix.Resolve(languageCode, L.CurrentMode);
    }

    /// <summary>The short word before the names in a Desktop panel's supporter line.</summary>
    public static string FormatThanks(string languageCode)
    {
        return Thanks.Resolve(languageCode, L.CurrentMode);
    }

    /// <summary>Who the supporter line thanks when it has no name to show.</summary>
    public static string FormatCommunity(string languageCode)
    {
        return Community.Resolve(languageCode, L.CurrentMode);
    }

    public static string FormatSponsorAction(string languageCode)
    {
        return SponsorAction.Resolve(languageCode, L.CurrentMode);
    }
}
