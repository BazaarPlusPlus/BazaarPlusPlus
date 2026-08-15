#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionSourceOfferPoolCacheKey
{
    public static string Build(CollectionSourceEntry source, EHero effectiveHero) =>
        string.Join(
            "|",
            source.SourceKey,
            BuildTemplateIdsFingerprint(source.SourceTemplateIds),
            source.OfferRuleFingerprint,
            TheDragonsHeroIdentity.TryCanonicalize(
                effectiveHero.ToString(),
                out var canonicalHeroId
            )
                ? canonicalHeroId
                : effectiveHero.ToString()
        );

    private static string BuildTemplateIdsFingerprint(IReadOnlyList<Guid> templateIds) =>
        string.Join(
            "-",
            templateIds.OrderBy(id => id).Select(id => id.ToString("N").Substring(0, 12))
        );
}
