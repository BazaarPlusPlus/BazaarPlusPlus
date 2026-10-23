using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Localization;
using Il2CppTheBazaar.Utilities;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// Facet chip labels on Android. Tags and keywords read the game's string table, the source the
/// desktop's <c>NativeTagTypography</c> falls back to; the desktop's keyword colors and icons come
/// from the tooltip typography table, which the Android panel does not read, so its chips are
/// text only. A reference keyword reads as its base keyword plus "Related", as on desktop.
/// </summary>
internal static class AndroidCollectionLabels
{
    private static readonly LocalizedTextSet RelatedSuffix = new(" Related", "相关", "相關");
    private static readonly LocalizedTextSet EconomyBase = new("Economy", "经济", "經濟");
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static string _language = string.Empty;

    public static string Tag(CardTag tag) => StringTable(tag.ToString());

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

    public static string Hero(CardHero hero) => CollectionPanelText.Hero(hero, TheDragonsName);

    private static string TheDragonsName()
    {
        try
        {
            if (AndroidCardFacets.Hero.TryToSource(CardHero.TheDragons, out var native))
            {
                var name = LocalizableShared.GetLocalizedHero(native);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception)
        {
            // The localization service is not up yet: the English name stands in.
        }
        return "The Dragons";
    }

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
        string label;
        try
        {
            var text = new LocalizableText(key).GetLocalizedText();
            label = string.IsNullOrEmpty(text) ? key : text;
        }
        catch (Exception)
        {
            return key; // not cached: the service may come up later
        }
        Cache[key] = label;
        return label;
    }
}
