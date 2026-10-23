#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public static class CollectionHeroScope
{
    public static bool MatchesFilter(CollectionCardVm card, CollectionFilterState filter)
    {
        var effectiveHero = filter.EffectiveHero;
        if (card.Type == CardType.Skill)
            return MatchesSkillHeroScope(card.Heroes, effectiveHero);

        return Contains(card.Heroes, effectiveHero);
    }

    // A skill matches the effective hero when it is hero-exclusive (exactly that hero,
    // including Common-only skills in neutral mode) or general-shared: a multi-hero skill
    // taught across heroes — never Common-scoped — that includes the selected hero.
    // The general-shared arm self-excludes Common ("contains Common" and "no Common" cannot
    // both hold), mirroring the in-game trainer pools that teach shared skills.
    public static bool MatchesSkillHeroScope(
        IReadOnlyCollection<CardHero> cardHeroes,
        CardHero hero
    )
    {
        if (cardHeroes.Count == 1 && Contains(cardHeroes, hero))
            return true;
        return cardHeroes.Count > 1
            && !Contains(cardHeroes, CardHero.Common)
            && Contains(cardHeroes, hero);
    }

    private static bool Contains(IReadOnlyCollection<CardHero> values, CardHero target)
    {
        foreach (var value in values)
        {
            if (value == target)
                return true;
        }
        return false;
    }
}
