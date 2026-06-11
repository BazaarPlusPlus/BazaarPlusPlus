#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionSourceOfferPoolCacheKey
{
    public static string Build(CollectionSourceEntry source, EHero? selectedHero) =>
        string.Join(
            "|",
            source.SourceKey,
            BuildTemplateIdsFingerprint(source.SourceTemplateIds),
            source.OfferRuleFingerprint,
            BuildHeroKey(selectedHero)
        );

    private static string BuildTemplateIdsFingerprint(IReadOnlyList<Guid> templateIds) =>
        string.Join(
            "-",
            templateIds.OrderBy(id => id).Select(id => id.ToString("N").Substring(0, 12))
        );

    private static string BuildHeroKey(EHero? selectedHero)
    {
        if (!selectedHero.HasValue)
            return "no-selected-hero";
        return selectedHero.Value.ToString();
    }
}
