#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCardClassification
{
    public bool IsCatalogCard { get; init; }

    public CollectionCardEligibilityReason EligibilityReason { get; init; }

    public bool IsPackage { get; init; }
}
