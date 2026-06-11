#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionHeroScope
{
    public static bool MatchesFilter(CollectionCardVm card, CollectionFilterState filter)
    {
        if (filter.Heroes.Count == 0)
            return true;

        if (card.Type == ECardType.Skill)
        {
            var selectedHero = filter.SelectedHero;
            return selectedHero.HasValue && MatchesSkillHeroScope(card.Heroes, selectedHero.Value);
        }

        return AnyHeroMatch(card.Heroes, filter.Heroes);
    }

    // A skill matches the selected hero when it is hero-exclusive (exactly that hero,
    // including Common-only skills under the Common chip) or general-shared: a multi-hero
    // skill taught across heroes — never Common-scoped — that includes the selected hero.
    // The general-shared arm self-excludes Common ("contains Common" and "no Common" cannot
    // both hold), mirroring the in-game trainer pools that teach shared skills.
    public static bool MatchesSkillHeroScope(IReadOnlyCollection<EHero> cardHeroes, EHero hero)
    {
        if (cardHeroes.Count == 1 && Contains(cardHeroes, hero))
            return true;
        return cardHeroes.Count > 1
            && !Contains(cardHeroes, EHero.Common)
            && Contains(cardHeroes, hero);
    }

    private static bool AnyHeroMatch(
        IReadOnlyCollection<EHero> cardHeroes,
        HashSet<EHero> filterHeroes
    )
    {
        foreach (var hero in cardHeroes)
        {
            if (filterHeroes.Contains(hero))
                return true;
        }
        return false;
    }

    private static bool Contains(IReadOnlyCollection<EHero> values, EHero target)
    {
        foreach (var value in values)
        {
            if (value == target)
                return true;
        }
        return false;
    }
}
