#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal static class ItemAttr
{
    public static IReadOnlyDictionary<string, int> GetAttributes(Guid templateId, string tier)
    {
        if (!EnsureLoaded())
            return new Dictionary<string, int>();

        if (
            !CardsJsonCache.TryGetSnapshot(out var snapshot)
            || !snapshot.AttributesByTemplateId.TryGetValue(templateId, out var card)
        )
            return new Dictionary<string, int>();

        var cappedTier = CardsJsonCache.NormalizeTier(tier);
        if (cappedTier == null)
            return new Dictionary<string, int>();

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tierName in CardsJsonCache.GetTierOrder())
        {
            if (CardsJsonCache.CompareTier(tierName, cappedTier) > 0)
                break;

            if (!card.AttributesByTier.TryGetValue(tierName, out var attributes))
                continue;

            foreach (var pair in attributes)
                result[pair.Key] = pair.Value;
        }

        return result;
    }

    internal static bool Warm()
    {
        return CardsJsonCache.Warm();
    }

    internal static void ResetForTests()
    {
        CardsJsonCache.ResetForTests();
    }

    private static bool EnsureLoaded()
    {
        try
        {
            return CardsJsonCache.TryGetSnapshot(out _);
        }
        catch (Exception ex)
        {
            BppLog.Error("ItemAttr", "Failed to load card attributes from shared cache", ex);
            return false;
        }
    }
}
