#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionCardEligibilityReason
{
    Accepted,
    UnsupportedType,
    MissingArtKey,
    InvalidArtKey,
    PlaceholderArtKey,
    MaterialArtKey,
    DebugTemplate,
    TemplateInternalName,
}

internal sealed class CollectionCardClassification
{
    public bool IsCatalogCard { get; init; }

    public CollectionCardEligibilityReason EligibilityReason { get; init; }

    public bool IsPackage { get; init; }
}
