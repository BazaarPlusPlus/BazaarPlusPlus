#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public enum CollectionCardEligibilityReason
{
    Accepted,
    UnsupportedType,
    NeverSpawnEligibility,
    MissingArtKey,
    InvalidArtKey,
    PlaceholderArtKey,
    MaterialArtKey,
    DebugTemplate,
    TemplateInternalName,
}

public static class CollectionCardEligibility
{
    public static CollectionCardEligibilityReason Classify(
        bool isItemOrSkill,
        bool neverSpawnEligible,
        string? artKey,
        string? internalName
    )
    {
        if (!isItemOrSkill)
            return CollectionCardEligibilityReason.UnsupportedType;
        if (neverSpawnEligible)
            return CollectionCardEligibilityReason.NeverSpawnEligibility;
        if (string.IsNullOrEmpty(artKey))
            return CollectionCardEligibilityReason.MissingArtKey;
        if (string.Equals(artKey, "Invalid", StringComparison.Ordinal))
            return CollectionCardEligibilityReason.InvalidArtKey;
        if (artKey!.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
            return CollectionCardEligibilityReason.PlaceholderArtKey;
        if (artKey.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            return CollectionCardEligibilityReason.MaterialArtKey;
        if (ContainsMarker(internalName, "[DEBUG]"))
            return CollectionCardEligibilityReason.DebugTemplate;
        if (ContainsMarker(internalName, "TEMPLATE"))
            return CollectionCardEligibilityReason.TemplateInternalName;
        return CollectionCardEligibilityReason.Accepted;
    }

    private static bool ContainsMarker(string? value, string marker) =>
        !string.IsNullOrWhiteSpace(value)
        && value!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
}
