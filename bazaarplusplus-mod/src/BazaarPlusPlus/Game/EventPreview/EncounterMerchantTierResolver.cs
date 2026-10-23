#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.EventPreview;

internal readonly struct EncounterMerchantTierPolicy
{
    public EncounterMerchantTierPolicy(CardTier? fixedTier, bool usesDayDistribution)
    {
        FixedTier = fixedTier;
        UsesDayDistribution = usesDayDistribution;
    }

    public CardTier? FixedTier { get; }

    public bool UsesDayDistribution { get; }
}

internal static class EncounterMerchantTierResolver
{
    public static EncounterMerchantTierPolicy Resolve(TCardBase? template)
    {
        if (
            template is not TCardEncounterEvent eventTemplate
            || eventTemplate.SelectionContext?.SpawnContext is not TSpawnContextQuery spawnContext
        )
            return new EncounterMerchantTierPolicy(fixedTier: null, usesDayDistribution: true);

        ETier? fixedTier = null;
        var ignoresDayTable = false;
        if (spawnContext.Behaviors == null)
            return new EncounterMerchantTierPolicy(fixedTier: null, usesDayDistribution: true);

        foreach (var behavior in spawnContext.Behaviors)
        {
            if (behavior is TSpawnBehaviorIgnoreTierTable { IgnoreTierTable: true })
                ignoresDayTable = true;

            if (
                behavior is TSpawnBehaviorTier { IsNot: false } tierBehavior
                && tierBehavior.Tiers.Count == 1
            )
            {
                foreach (var tier in tierBehavior.Tiers)
                    fixedTier = tier;
            }
        }

        // A pinned tier with no facet (vocabulary drift the pin tests report) is still pinned:
        // neither a fixed tier line nor the day distribution describes it.
        if (fixedTier is { } pinned)
            return new EncounterMerchantTierPolicy(
                DesktopCardFacets.Tier.TryToFacet(pinned, out var facet) ? facet : null,
                usesDayDistribution: false
            );
        return new EncounterMerchantTierPolicy(
            fixedTier: null,
            usesDayDistribution: !ignoresDayTable
        );
    }
}
