#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCardEnchantmentFacets
{
    public CollectionCardEnchantmentFacets(
        EEnchantmentType type,
        IReadOnlyCollection<ECardTag> tags,
        IReadOnlyCollection<EHiddenTag> hiddenTags
    )
    {
        Type = type;
        Tags = tags ?? Array.Empty<ECardTag>();
        HiddenTags = hiddenTags ?? Array.Empty<EHiddenTag>();
    }

    public EEnchantmentType Type { get; }

    public IReadOnlyCollection<ECardTag> Tags { get; }

    public IReadOnlyCollection<EHiddenTag> HiddenTags { get; }
}

// Immutable projection of a card template into the fields the catalog/filters/virtualizer need.
// Single shared instance per Guid; filter engine and grid pass the same reference around. The
// `From(TCardBase)` factory lives in CollectionCardVm.From.cs so this half stays free of the
// game card-model types and can be compiled into the CollectionGridLayout unit test.
internal sealed partial class CollectionCardVm
{
    public Guid Id { get; init; }
    public ECardType Type { get; init; }
    public ECardSize Size { get; init; }
    public ETier StartingTier { get; init; }
    public IReadOnlyCollection<EHero> Heroes { get; init; } = Array.Empty<EHero>();
    public IReadOnlyCollection<ECardTag> Tags { get; init; } = Array.Empty<ECardTag>();
    public IReadOnlyCollection<EHiddenTag> HiddenTags { get; init; } = Array.Empty<EHiddenTag>();
    public string DisplayName { get; init; } = string.Empty;
    public string InternalName { get; init; } = string.Empty;
    public string ArtKey { get; init; } = string.Empty;
    public bool IsEnchantable { get; init; }
    public IReadOnlyDictionary<
        EEnchantmentType,
        CollectionCardEnchantmentFacets
    > Enchantments { get; init; } =
        new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>();
    public bool IsPackage { get; init; }
}
