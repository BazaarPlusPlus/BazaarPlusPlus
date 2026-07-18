#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarGameShared.Domain.Spawning.SpawningContexts;

namespace BazaarPlusPlus.Game.EventPreview;

internal readonly struct EncounterMerchantTierPolicy
{
    public EncounterMerchantTierPolicy(ETier? fixedTier, bool usesDayDistribution)
    {
        FixedTier = fixedTier;
        UsesDayDistribution = usesDayDistribution;
    }

    public ETier? FixedTier { get; }

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

        return fixedTier.HasValue
            ? new EncounterMerchantTierPolicy(fixedTier, usesDayDistribution: false)
            : new EncounterMerchantTierPolicy(
                fixedTier: null,
                usesDayDistribution: !ignoresDayTable
            );
    }
}
