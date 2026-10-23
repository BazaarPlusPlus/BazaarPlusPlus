#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Centralizes the catalog/filter facts that are not direct UI selections.
internal static class CollectionCardClassifier
{
    public static bool IsCatalogCard(TCardBase template) => Classify(template).IsCatalogCard;

    public static bool IsCatalogCard(
        ECardType type,
        ESpawnEligibility spawningEligibility,
        string? artKey,
        string? internalName
    )
    {
        return Classify(type, spawningEligibility, artKey, internalName).IsCatalogCard;
    }

    public static CollectionCardClassification Classify(TCardBase template)
    {
        var classification = Classify(
            template.Type,
            template.SpawningEligibility,
            template.ArtKey,
            template.InternalName
        );
        return new CollectionCardClassification
        {
            IsCatalogCard = classification.IsCatalogCard,
            EligibilityReason = classification.EligibilityReason,
            IsPackage = IsPackage(template),
        };
    }

    public static CollectionCardClassification Classify(
        ECardType type,
        ESpawnEligibility spawningEligibility,
        string? artKey,
        string? internalName
    )
    {
        var reason = CollectionCardEligibility.Classify(
            type == ECardType.Item || type == ECardType.Skill,
            spawningEligibility == ESpawnEligibility.Never,
            artKey,
            internalName
        );
        return new CollectionCardClassification
        {
            IsCatalogCard = reason == CollectionCardEligibilityReason.Accepted,
            EligibilityReason = reason,
        };
    }

    public static bool IsPackage(TCardBase template) =>
        PackageIdentity.IsPackage(template.HiddenTags);
}
