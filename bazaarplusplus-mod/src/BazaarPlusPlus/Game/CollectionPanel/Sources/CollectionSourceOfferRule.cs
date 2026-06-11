#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class CollectionSourceStartingTierRule
{
    public CollectionSourceStartingTierRule(CollectionSourceStartingTierMode mode, ETier tier)
    {
        Mode = mode;
        Tier = tier;
    }

    public CollectionSourceStartingTierMode Mode { get; }

    public ETier Tier { get; }
}

internal sealed class CollectionSourceOfferRule
{
    public CollectionSourceOfferRule(
        CollectionSourceHeroMode heroMode,
        EHero? hero,
        CollectionSourceStartingTierRule? startingTier,
        IReadOnlyList<ECardSize> sizesAny,
        IReadOnlyList<ECardTag> tagsAny,
        IReadOnlyList<ECardTag> tagsNone,
        IReadOnlyList<EHiddenTag> hiddenTagsAny,
        bool enchantableOnly,
        IReadOnlyList<EEnchantmentType> enchantmentTypesAny,
        IReadOnlyList<ECardTag> enchantmentTagsAny,
        IReadOnlyList<EHiddenTag> enchantmentHiddenTagsAny
    )
    {
        HeroMode = heroMode;
        Hero = hero;
        StartingTier = startingTier;
        SizesAny = sizesAny ?? Array.Empty<ECardSize>();
        TagsAny = tagsAny ?? Array.Empty<ECardTag>();
        TagsNone = tagsNone ?? Array.Empty<ECardTag>();
        HiddenTagsAny = hiddenTagsAny ?? Array.Empty<EHiddenTag>();
        EnchantableOnly = enchantableOnly;
        EnchantmentTypesAny = enchantmentTypesAny ?? Array.Empty<EEnchantmentType>();
        EnchantmentTagsAny = enchantmentTagsAny ?? Array.Empty<ECardTag>();
        EnchantmentHiddenTagsAny = enchantmentHiddenTagsAny ?? Array.Empty<EHiddenTag>();
    }

    public CollectionSourceHeroMode HeroMode { get; }

    public EHero? Hero { get; }

    public CollectionSourceStartingTierRule? StartingTier { get; }

    public IReadOnlyList<ECardSize> SizesAny { get; }

    public IReadOnlyList<ECardTag> TagsAny { get; }

    public IReadOnlyList<ECardTag> TagsNone { get; }

    public IReadOnlyList<EHiddenTag> HiddenTagsAny { get; }

    public bool EnchantableOnly { get; }

    public IReadOnlyList<EEnchantmentType> EnchantmentTypesAny { get; }

    public IReadOnlyList<ECardTag> EnchantmentTagsAny { get; }

    public IReadOnlyList<EHiddenTag> EnchantmentHiddenTagsAny { get; }
}

internal sealed class CollectionSourceOfferSegment
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

internal sealed class CollectionSourceOfferMatch
{
    public CollectionSourceOfferMatch(
        string segmentKey,
        CollectionSourceOfferSegmentKind segmentKind,
        string rarityLabel,
        EEnchantmentType? enchantmentType
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

    public EEnchantmentType? EnchantmentType { get; }
}
