#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public static class CollectionHeroSelectionRoster
{
    public static IReadOnlyList<CardHero> BaseConcreteHeroes { get; } =
        Array.AsReadOnly(
            new[]
            {
                CardHero.Vanessa,
                CardHero.Dooley,
                CardHero.Pygmalien,
                CardHero.Karnok,
                CardHero.Mak,
                CardHero.Stelle,
                CardHero.Jules,
            }
        );

    // The Dragons join the roster only when the accepted catalog deals them a card. A card can
    // carry CardHero.TheDragons only when the host mapped it from this game build's hero enum,
    // so a build without the hero never lists it.
    public static IReadOnlyList<CardHero> ResolveAvailableHeroes(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        if (catalogCards == null)
            throw new ArgumentNullException(nameof(catalogCards));
        if (catalogReadiness != CollectionCatalogReadiness.Accepted)
            return BaseConcreteHeroes;

        return IncludeTheDragonsWhenPresent(catalogCards);
    }

    public static CardHero? NormalizeSelection(
        CardHero? selectedHero,
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<CardHero> availableHeroes
    )
    {
        if (availableHeroes == null)
            throw new ArgumentNullException(nameof(availableHeroes));
        if (!selectedHero.HasValue || selectedHero.Value == CardHero.Common)
            return null;
        if (catalogReadiness != CollectionCatalogReadiness.Accepted)
            return selectedHero;
        return availableHeroes.Contains(selectedHero.Value) ? selectedHero : null;
    }

    private static IReadOnlyList<CardHero> IncludeTheDragonsWhenPresent(
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        foreach (var card in catalogCards)
        {
            if (card.IsPackage)
                continue;
            if (card.Type == CardType.Skill)
            {
                if (CollectionHeroScope.MatchesSkillHeroScope(card.Heroes, CardHero.TheDragons))
                    return WithTheDragons;
                continue;
            }

            if (card.Heroes.Contains(CardHero.TheDragons))
                return WithTheDragons;
        }

        return BaseConcreteHeroes;
    }

    private static readonly IReadOnlyList<CardHero> WithTheDragons = Array.AsReadOnly(
        BaseConcreteHeroes.Append(CardHero.TheDragons).ToArray()
    );
}
