#nullable enable
using System;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Centralizes the catalog/filter facts that are not direct UI selections.
internal static class CollectionCardClassifier
{
    public static bool IsCatalogCard(TCardBase template) => Classify(template).IsCatalogCard;

    public static bool IsCatalogCard(ECardType type, string? artKey, string? internalName)
    {
        return Classify(type, artKey, internalName).IsCatalogCard;
    }

    public static CollectionCardClassification Classify(TCardBase template)
    {
        var classification = Classify(template.Type, template.ArtKey, template.InternalName);
        return new CollectionCardClassification
        {
            IsCatalogCard = classification.IsCatalogCard,
            EligibilityReason = classification.EligibilityReason,
            IsPackage = IsPackage(template),
        };
    }

    public static CollectionCardClassification Classify(
        ECardType type,
        string? artKey,
        string? internalName
    )
    {
        if (type != ECardType.Item && type != ECardType.Skill)
            return Rejected(CollectionCardEligibilityReason.UnsupportedType, internalName);
        if (string.IsNullOrEmpty(artKey))
            return Rejected(CollectionCardEligibilityReason.MissingArtKey, internalName);
        if (string.Equals(artKey, "Invalid", StringComparison.Ordinal))
            return Rejected(CollectionCardEligibilityReason.InvalidArtKey, internalName);
        if (artKey.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
            return Rejected(CollectionCardEligibilityReason.PlaceholderArtKey, internalName);
        if (artKey.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            return Rejected(CollectionCardEligibilityReason.MaterialArtKey, internalName);
        if (ContainsMarker(internalName, "[DEBUG]"))
            return Rejected(CollectionCardEligibilityReason.DebugTemplate, internalName);
        if (ContainsMarker(internalName, "TEMPLATE"))
            return Rejected(CollectionCardEligibilityReason.TemplateInternalName, internalName);

        return new CollectionCardClassification
        {
            IsCatalogCard = true,
            EligibilityReason = CollectionCardEligibilityReason.Accepted,
        };
    }

    public static bool IsPackage(TCardBase template) =>
        PackageIdentity.IsPackage(template.HiddenTags);

    private static CollectionCardClassification Rejected(
        CollectionCardEligibilityReason reason,
        string? internalName
    ) => new() { IsCatalogCard = false, EligibilityReason = reason };

    private static bool ContainsMarker(string? value, string marker)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
