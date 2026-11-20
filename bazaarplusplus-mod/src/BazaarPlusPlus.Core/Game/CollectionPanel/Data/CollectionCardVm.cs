#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public sealed class CollectionCardEnchantmentFacets
{
    public CollectionCardEnchantmentFacets(
        CardEnchantment type,
        IReadOnlyCollection<CardTag> tags,
        IReadOnlyCollection<CardHiddenTag> hiddenTags
    )
    {
        Type = type;
        Tags = tags ?? Array.Empty<CardTag>();
        HiddenTags = hiddenTags ?? Array.Empty<CardHiddenTag>();
    }

    public CardEnchantment Type { get; }

    public IReadOnlyCollection<CardTag> Tags { get; }

    public IReadOnlyCollection<CardHiddenTag> HiddenTags { get; }
}

public sealed class CollectionCardVm
{
    public Guid Id { get; init; }
    public CardType Type { get; init; }
    public CardSize Size { get; init; }
    public CardTier StartingTier { get; init; }
    public IReadOnlyCollection<CardHero> Heroes { get; init; } = Array.Empty<CardHero>();
    public IReadOnlyCollection<CardTag> Tags { get; init; } = Array.Empty<CardTag>();
    public IReadOnlyCollection<CardHiddenTag> HiddenTags { get; init; } =
        Array.Empty<CardHiddenTag>();
    public CollectionMechanic Mechanics { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string InternalName { get; init; } = string.Empty;
    public string ArtKey { get; init; } = string.Empty;
    public string SearchText { get; init; } = string.Empty;
    public bool IsEnchantable { get; init; }
    public IReadOnlyDictionary<
        CardEnchantment,
        CollectionCardEnchantmentFacets
    > Enchantments { get; init; } =
        new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>();
    public bool IsPackage { get; init; }

    // Filter clicks rerun the search predicate for every card. Cache derivations from the immutable fields to avoid repeated scans and allocations.
    private string? _normalizedSearchCorpus;
    private string? _compactSearchCorpus;
    private string[]? _initialismKeys;

    public string NormalizedSearchCorpus =>
        _normalizedSearchCorpus ??= CollectionCardSearch.Normalize(
            string.IsNullOrWhiteSpace(SearchText)
                ? CollectionCardSearch.BuildCorpus(this)
                : SearchText
        );

    public string CompactSearchCorpus =>
        _compactSearchCorpus ??= CollectionCardSearch.Compact(NormalizedSearchCorpus);

    public string[] InitialismKeys =>
        _initialismKeys ??= CollectionCardSearch.BuildInitialismKeys(this);

    private int? _heroMask;

    internal int HeroMask => _heroMask ??= CollectionHeroScope.MaskOf(Heroes);

    internal CollectionCardNameOrder? NameOrder { get; set; }

    internal int NameRank { get; set; }
}
