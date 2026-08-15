#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionPanelOpenSelectionResolver
{
    public static CollectionPanelSelectionState Resolve(
        bool isInGameRun,
        EHero? currentHero,
        Guid? currentEncounterTemplateId,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries,
        CollectionPanelHeroPreferenceLoadResult? rememberedPreference = null
    )
    {
        if (!isInGameRun)
            return ResolveOutOfRunSelection(rememberedPreference);

        if (!IsConcreteHero(currentHero))
            return CollectionPanelSelectionState.Default;

        var hero = currentHero!.Value;
        var source = ResolveSource(
            hero,
            currentEncounterTemplateId,
            choiceSelectionTemplateIds,
            entries
        );

        return source == null
            ? new CollectionPanelSelectionState(
                hero,
                CollectionPanelSelectionState.DefaultMerchantSourceKey,
                CollectionSourceKind.Merchant
            )
            : new CollectionPanelSelectionState(hero, source.SourceKey, source.Kind);
    }

    internal static bool IsConcreteHero(EHero? hero) => hero.HasValue && hero.Value != EHero.Common;

    public static IReadOnlyList<string> ResolveEncounteredMerchantSourceKeys(
        EHero? currentHero,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        if (
            !IsConcreteHero(currentHero)
            || choiceSelectionTemplateIds == null
            || choiceSelectionTemplateIds.Count == 0
        )
            return Array.Empty<string>();

        var hero = currentHero!.Value;
        var sourceKeys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var templateId in choiceSelectionTemplateIds)
        {
            if (templateId == Guid.Empty)
                continue;

            var source = FindSource(
                hero,
                templateId,
                entries,
                requiredKind: CollectionSourceKind.Merchant
            );
            if (source != null && seen.Add(source.SourceKey))
                sourceKeys.Add(source.SourceKey);
        }

        return sourceKeys;
    }

    private static CollectionPanelSelectionState ResolveOutOfRunSelection(
        CollectionPanelHeroPreferenceLoadResult? rememberedPreference
    )
    {
        var status = rememberedPreference?.Status ?? CollectionPanelHeroPreferenceLoadStatus.Absent;
        var hero = status switch
        {
            CollectionPanelHeroPreferenceLoadStatus.Resolved => rememberedPreference!.Hero,
            CollectionPanelHeroPreferenceLoadStatus.KnownUnavailable => null,
            _ => CollectionPanelSelectionState.DefaultHero,
        };

        return new CollectionPanelSelectionState(
            hero,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            CollectionSourceKind.Merchant
        );
    }

    private static CollectionSourceEntry? ResolveSource(
        EHero hero,
        Guid? currentEncounterTemplateId,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        if (currentEncounterTemplateId.HasValue)
        {
            var currentSource = FindSource(hero, currentEncounterTemplateId.Value, entries);
            if (currentSource != null)
                return currentSource;
        }

        if (choiceSelectionTemplateIds == null || choiceSelectionTemplateIds.Count == 0)
            return null;

        foreach (var templateId in choiceSelectionTemplateIds)
        {
            if (templateId == Guid.Empty)
                continue;

            var source = FindSource(hero, templateId, entries);
            if (source != null)
                return source;
        }

        return null;
    }

    private static CollectionSourceEntry? FindSource(
        EHero hero,
        Guid templateId,
        IEnumerable<CollectionSourceEntry> entries,
        CollectionSourceKind? requiredKind = null
    )
    {
        foreach (var entry in entries)
        {
            if (requiredKind.HasValue && entry.Kind != requiredKind.Value)
                continue;
            if (!entry.AppliesToHero(hero))
                continue;

            foreach (var candidate in entry.SourceTemplateIds)
                if (candidate == templateId)
                    return entry;
        }

        return null;
    }
}
