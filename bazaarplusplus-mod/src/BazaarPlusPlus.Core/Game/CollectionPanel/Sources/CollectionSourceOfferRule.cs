#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public sealed class CollectionSourceStartingTierRule
{
    public CollectionSourceStartingTierRule(CollectionSourceStartingTierMode mode, CardTier tier)
    {
        Mode = mode;
        Tier = tier;
    }

    public CollectionSourceStartingTierMode Mode { get; }

    public CardTier Tier { get; }
}

public sealed class CollectionSourceOfferRule
{
    public CollectionSourceOfferRule(
        CollectionSourceHeroMode heroMode,
        CardHero? hero,
        CollectionSourceStartingTierRule? startingTier,
        IReadOnlyList<CardSize> sizesAny,
        IReadOnlyList<CardTag> tagsAny,
        IReadOnlyList<CardTag> tagsNone,
        IReadOnlyList<CardHiddenTag> hiddenTagsAny,
        bool enchantableOnly,
        IReadOnlyList<CardEnchantment> enchantmentTypesAny,
        IReadOnlyList<CardTag> enchantmentTagsAny,
        IReadOnlyList<CardHiddenTag> enchantmentHiddenTagsAny
    )
    {
        HeroMode = heroMode;
        Hero = hero;
        StartingTier = startingTier;
        SizesAny = sizesAny ?? Array.Empty<CardSize>();
        TagsAny = tagsAny ?? Array.Empty<CardTag>();
        TagsNone = tagsNone ?? Array.Empty<CardTag>();
        HiddenTagsAny = hiddenTagsAny ?? Array.Empty<CardHiddenTag>();
        EnchantableOnly = enchantableOnly;
        EnchantmentTypesAny = enchantmentTypesAny ?? Array.Empty<CardEnchantment>();
        EnchantmentTagsAny = enchantmentTagsAny ?? Array.Empty<CardTag>();
        EnchantmentHiddenTagsAny = enchantmentHiddenTagsAny ?? Array.Empty<CardHiddenTag>();
    }

    public CollectionSourceHeroMode HeroMode { get; }

    public CardHero? Hero { get; }

    public CollectionSourceStartingTierRule? StartingTier { get; }

    public IReadOnlyList<CardSize> SizesAny { get; }

    public IReadOnlyList<CardTag> TagsAny { get; }

    public IReadOnlyList<CardTag> TagsNone { get; }

    public IReadOnlyList<CardHiddenTag> HiddenTagsAny { get; }

    public bool EnchantableOnly { get; }

    public IReadOnlyList<CardEnchantment> EnchantmentTypesAny { get; }

    public IReadOnlyList<CardTag> EnchantmentTagsAny { get; }

    public IReadOnlyList<CardHiddenTag> EnchantmentHiddenTagsAny { get; }
}

public sealed class CollectionSourceOfferSegment
{
    public CollectionSourceOfferSegment(
        string key,
        CollectionSourceOfferSegmentKind kind,
        string rarityLabel,
        CollectionSourceOfferRule rule
    )
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Kind = kind;
        RarityLabel = rarityLabel ?? string.Empty;
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
    }

    public string Key { get; }

    public CollectionSourceOfferSegmentKind Kind { get; }

    public string RarityLabel { get; }

    public CollectionSourceOfferRule Rule { get; }
}

public sealed class CollectionSourceOfferMatch
{
    public CollectionSourceOfferMatch(
        string segmentKey,
        CollectionSourceOfferSegmentKind segmentKind,
        string rarityLabel,
        CardEnchantment? enchantmentType
    )
    {
        SegmentKey = segmentKey ?? throw new ArgumentNullException(nameof(segmentKey));
        SegmentKind = segmentKind;
        RarityLabel = rarityLabel ?? string.Empty;
        EnchantmentType = enchantmentType;
    }

    public string SegmentKey { get; }

    public CollectionSourceOfferSegmentKind SegmentKind { get; }

    public string RarityLabel { get; }

    public CardEnchantment? EnchantmentType { get; }
}
