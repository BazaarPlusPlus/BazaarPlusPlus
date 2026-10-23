#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

public static class CollectionSourceOfferPoolCacheKey
{
    public static string Build(CollectionSourceEntry source, CardHero effectiveHero) =>
        string.Join(
            "|",
            source.SourceKey,
            BuildTemplateIdsFingerprint(source.SourceTemplateIds),
            source.OfferRuleFingerprint,
            // A hero facet's name is its canonical id (ADR-0015).
            effectiveHero.ToString()
        );

    private static string BuildTemplateIdsFingerprint(IReadOnlyList<Guid> templateIds) =>
        string.Join(
            "-",
            templateIds.OrderBy(id => id).Select(id => id.ToString("N").Substring(0, 12))
        );
}
