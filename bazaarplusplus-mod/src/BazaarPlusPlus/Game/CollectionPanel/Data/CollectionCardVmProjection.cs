#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// The desktop host's projection of a live TCardBase template into the Core CollectionCardVm,
// mapping every game enum through DesktopCardFacets (ADR-0015).
internal static class CollectionCardVmProjection
{
    public static CollectionCardVm? From(TCardBase template) =>
        From(template, CollectionCardClassifier.Classify(template));

    // Null when the card's type, size, or starting tier has no card facet: a game member BPP has
    // not added yet, which the catalog counts as rejected rather than guessing a facet.
    internal static CollectionCardVm? From(
        TCardBase template,
        CollectionCardClassification classification
    )
    {
        if (
            !DesktopCardFacets.Type.TryToFacet(template.Type, out var type)
            || !DesktopCardFacets.Size.TryToFacet(template.Size, out var size)
            || !DesktopCardFacets.Tier.TryToFacet(template.StartingTier, out var startingTier)
        )
            return null;

        var titleSearchTexts = CollectionLocalizationResolver.ResolveTitleSearchTexts(template);
        var displayName = FirstOrEmpty(titleSearchTexts);
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = template.InternalName;
        var descriptionSearchTexts = CollectionLocalizationResolver.ResolveDescriptionSearchTexts(
            template
        );
        var tooltipSearchTexts = CollectionLocalizationResolver.ResolveTooltipSearchTexts(template);
        var description = FirstOrEmpty(descriptionSearchTexts);
        var searchableContent = string.Join(" ", descriptionSearchTexts);
        if (tooltipSearchTexts.Count > 0)
            searchableContent = string.Concat(
                searchableContent,
                " ",
                string.Join(" ", tooltipSearchTexts)
            );
        var heroes = DesktopCardFacets.Hero.ToFacetSet(template.Heroes);
        var tags = DesktopCardFacets.Tag.ToFacetSet(template.Tags);
        var hiddenTags = DesktopCardFacets.HiddenTag.ToFacetSet(
            CollectionDerivedKeywordFacts.ProjectHiddenTags(template)
        );
        var mechanics = CollectionMechanicFacts.Project(template);
        var enchantments =
            template is TCardItem item && item.Enchantments != null
                ? ProjectEnchantments(item.Enchantments)
                : new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>();

        var vm = new CollectionCardVm
        {
            Id = template.Id,
            Type = type,
            Size = size,
            StartingTier = startingTier,
            Heroes = heroes,
            Tags = tags,
            HiddenTags = hiddenTags,
            Mechanics = mechanics,
            DisplayName = displayName,
            Description = description,
            InternalName = template.InternalName,
            ArtKey = template.ArtKey,
            IsEnchantable = enchantments.Count > 0,
            Enchantments = enchantments,
            IsPackage = classification.IsPackage,
            SearchText = CollectionCardSearch.BuildCorpus(
                new CollectionCardVm
                {
                    Id = template.Id,
                    Type = type,
                    Size = size,
                    StartingTier = startingTier,
                    Heroes = heroes,
                    Tags = tags,
                    HiddenTags = hiddenTags,
                    Mechanics = mechanics,
                    DisplayName = string.Join(" ", titleSearchTexts),
                    Description = searchableContent,
                    InternalName = template.InternalName,
                    ArtKey = template.ArtKey,
                    IsEnchantable = enchantments.Count > 0,
                    Enchantments = enchantments,
                    IsPackage = classification.IsPackage,
                }
            ),
        };
        return vm;
    }

    private static string FirstOrEmpty(IReadOnlyList<string> values) =>
        values.Count > 0 ? values[0] : string.Empty;

    private static IReadOnlyDictionary<
        CardEnchantment,
        CollectionCardEnchantmentFacets
    > ProjectEnchantments(IReadOnlyDictionary<EEnchantmentType, TEnchantment> source)
    {
        var result = new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>(source.Count);
        foreach (var pair in source)
        {
            if (!DesktopCardFacets.Enchantment.TryToFacet(pair.Key, out var type))
                continue;
            var enchantment = pair.Value;
            result[type] = new CollectionCardEnchantmentFacets(
                type,
                DesktopCardFacets.Tag.ToFacetSet(enchantment?.Tags),
                DesktopCardFacets.HiddenTag.ToFacetSet(enchantment?.HiddenTags)
            );
        }
        return result;
    }
}
