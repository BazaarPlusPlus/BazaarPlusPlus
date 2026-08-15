#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionHeroSelectionRoster
{
    public static IReadOnlyList<EHero> BaseConcreteHeroes { get; } =
        Array.AsReadOnly(
            new[]
            {
                EHero.Vanessa,
                EHero.Dooley,
                EHero.Pygmalien,
                EHero.Karnok,
                EHero.Mak,
                EHero.Stelle,
                EHero.Jules,
            }
        );

    public static IReadOnlyList<EHero> ResolveAvailableHeroes(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyList<CollectionCardVm> catalogCards
    ) =>
        ResolveAvailableHeroesCore(
            catalogReadiness,
            catalogCards,
            () =>
                TheDragonsHeroIdentity.TryResolve(TheDragonsHeroIdentity.CanonicalId, out var hero)
                    ? hero
                    : null
        );

    internal static IReadOnlyList<EHero> ResolveAvailableHeroes(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyList<CollectionCardVm> catalogCards,
        Func<string, EHero?> resolveExactName
    )
    {
        if (resolveExactName == null)
            throw new ArgumentNullException(nameof(resolveExactName));

        return ResolveAvailableHeroesCore(
            catalogReadiness,
            catalogCards,
            () =>
                TheDragonsHeroIdentity.TryResolve(
                    TheDragonsHeroIdentity.CanonicalId,
                    resolveExactName,
                    out var hero
                )
                    ? hero
                    : null
        );
    }

    private static IReadOnlyList<EHero> ResolveAvailableHeroesCore(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyList<CollectionCardVm> catalogCards,
        Func<EHero?> resolveTheDragons
    )
    {
        if (catalogCards == null)
            throw new ArgumentNullException(nameof(catalogCards));
        if (resolveTheDragons == null)
            throw new ArgumentNullException(nameof(resolveTheDragons));
        if (catalogReadiness != CollectionCatalogReadiness.Accepted)
            return BaseConcreteHeroes;
        var dragonsHero = resolveTheDragons();
        if (!dragonsHero.HasValue)
            return BaseConcreteHeroes;

        return IncludeTheDragonsWhenPresent(catalogCards, dragonsHero.Value);
    }

    public static EHero? NormalizeSelection(
        EHero? selectedHero,
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<EHero> availableHeroes
    )
    {
        if (availableHeroes == null)
            throw new ArgumentNullException(nameof(availableHeroes));
        if (!selectedHero.HasValue || selectedHero.Value == EHero.Common)
            return null;
        if (catalogReadiness != CollectionCatalogReadiness.Accepted)
            return selectedHero;
        return availableHeroes.Contains(selectedHero.Value) ? selectedHero : null;
    }

    private static IReadOnlyList<EHero> IncludeTheDragonsWhenPresent(
        IReadOnlyList<CollectionCardVm> catalogCards,
        EHero dragonsHero
    )
    {
        foreach (var card in catalogCards)
        {
            if (card.IsPackage)
                continue;
            if (card.Type == ECardType.Skill)
            {
                if (CollectionHeroScope.MatchesSkillHeroScope(card.Heroes, dragonsHero))
                    return AppendTheDragons(dragonsHero);
                continue;
            }

            foreach (var hero in card.Heroes)
            {
                if (hero != dragonsHero && !TheDragonsHeroIdentity.IsTheDragons(hero))
                    continue;

                return AppendTheDragons(dragonsHero);
            }
        }

        return BaseConcreteHeroes;
    }

    private static IReadOnlyList<EHero> AppendTheDragons(EHero dragonsHero)
    {
        var result = new EHero[BaseConcreteHeroes.Count + 1];
        for (var i = 0; i < BaseConcreteHeroes.Count; i++)
            result[i] = BaseConcreteHeroes[i];
        result[^1] = dragonsHero;
        return Array.AsReadOnly(result);
    }
}
