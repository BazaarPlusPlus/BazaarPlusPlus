#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Game-card-model side of CollectionCardVm: projects a live TCardBase template into the VM.
// Kept separate from the POCO half so the layout test can compile CollectionCardVm.cs without
// dragging in TCardBase / CollectionLocalizationResolver.
internal sealed partial class CollectionCardVm
{
    public static CollectionCardVm From(TCardBase template) =>
        From(template, CollectionCardClassifier.Classify(template));

    internal static CollectionCardVm From(
        TCardBase template,
        CollectionCardClassification classification
    )
    {
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
        var hiddenTags = CollectionDerivedKeywordFacts.ProjectHiddenTags(template);
        var enchantments =
            template is TCardItem item && item.Enchantments != null
                ? ProjectEnchantments(item.Enchantments)
                : new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>();

        var vm = new CollectionCardVm
        {
            Id = template.Id,
            Type = template.Type,
            Size = template.Size,
            StartingTier = template.StartingTier,
            Heroes = template.Heroes,
            Tags = template.Tags,
            HiddenTags = hiddenTags,
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
                    Type = template.Type,
                    Size = template.Size,
                    StartingTier = template.StartingTier,
                    Heroes = template.Heroes,
                    Tags = template.Tags,
                    HiddenTags = hiddenTags,
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
        EEnchantmentType,
        CollectionCardEnchantmentFacets
    > ProjectEnchantments(IReadOnlyDictionary<EEnchantmentType, TEnchantment> source)
    {
        if (source.Count == 0)
            return new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>();

        var result = new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>(
            source.Count
        );
        foreach (var pair in source)
        {
            var enchantment = pair.Value;
            result[pair.Key] = new CollectionCardEnchantmentFacets(
                pair.Key,
                OrEmpty(enchantment?.Tags),
                OrEmpty(enchantment?.HiddenTags)
            );
        }
        return result;
    }

    private static IReadOnlyCollection<T> OrEmpty<T>(IReadOnlyCollection<T>? values) =>
        values ?? Array.Empty<T>();
}
