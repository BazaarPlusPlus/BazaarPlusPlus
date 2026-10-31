using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Mobile.Collection;

/// <summary>
/// Filter labels read the game's string table. Inspection tags additionally use the native
/// tooltip typography for official labels and colors. A reference keyword reads as its base
/// keyword plus "Related", as on desktop.
/// </summary>
internal static class TouchCollectionLabels
{
    private static readonly LocalizedTextSet RelatedSuffix = new(" Related", "相关", "相關");
    private static readonly LocalizedTextSet EconomyBase = new("Economy", "经济", "經濟");
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static string _language = string.Empty;

    public static string Tag(CardTag tag) => StringTable(tag.ToString());

    /// <summary>The same localized label and accent as the game's tooltip tag renderer.</summary>
    public static string InspectionTag(CardTag tag)
    {
        var native = MobilePlatform.Port.TooltipTag(tag.ToString());
        return string.IsNullOrWhiteSpace(native) ? Tag(tag) : native!;
    }

    public static string Keyword(CollectionKeywordFacetOption option)
    {
        if (option.Keyword is { } keyword)
            return Keyword(keyword);
        return option.Mechanic switch
        {
            CollectionMechanic.Multicast => Keyword(CardHiddenTag.Multicast),
            CollectionMechanic.Destroy => StringTable("Destroy"),
            _ => option.Mechanic?.ToString() ?? string.Empty,
        };
    }

    public static string Keyword(CardHiddenTag keyword)
    {
        if (keyword == CardHiddenTag.EconomyReference)
            return L.Resolve(EconomyBase) + L.Resolve(RelatedSuffix);
        if (CardReferenceTags.TryResolveBase(keyword, out var referenceBase))
        {
            var baseLabel =
                referenceBase.HiddenTag is { } hidden ? Keyword(hidden)
                : referenceBase.CardTag is { } tag ? Tag(tag)
                : keyword.ToString();
            return baseLabel + L.Resolve(RelatedSuffix);
        }
        var label = StringTable(keyword.ToString());
        return keyword == CardHiddenTag.Quest
            ? L.Resolve(new LocalizedTextSet(label, "任务"))
            : label;
    }

    public static string Enchantment(CardEnchantment enchantment) =>
        StringTable(enchantment.ToString());

    public static string Hero(CardHero hero) =>
        hero == CardHero.Common
            ? CollectionPanelText.Hero(
                hero,
                () => MobilePlatform.Port.HeroName(CardHero.TheDragons)
            )
            : MobilePlatform.Port.HeroName(hero);

    // The game string table keyed by the English source text; a miss returns the key itself.
    private static string StringTable(string key)
    {
        var language = L.CurrentLanguageCode;
        if (!string.Equals(language, _language, StringComparison.Ordinal))
        {
            Cache.Clear();
            _language = language;
        }
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var text = MobilePlatform.Port.GameText(key);
        if (text == null)
            return key; // not cached: the service may come up later
        var label = text.Length == 0 ? key : text;
        Cache[key] = label;
        return label;
    }
}
