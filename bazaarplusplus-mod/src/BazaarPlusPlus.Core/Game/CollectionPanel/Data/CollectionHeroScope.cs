#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public static class CollectionHeroScope
{
    // One bit per CardHero member. The sign bit marks a member past the 31 a mask holds, which
    // sends the lookup back to the collection.
    private const int UnmappedBit = int.MinValue;

    public static bool MatchesFilter(CollectionCardVm card, CollectionFilterState filter)
    {
        var effectiveHero = filter.EffectiveHero;
        if (card.Type == CardType.Skill)
            return MatchesSkillHeroScope(card.Heroes, card.HeroMask, effectiveHero);

        return Contains(card.Heroes, card.HeroMask, effectiveHero);
    }

    // The game's shared trainer pools cover multiple heroes while excluding Common.
    public static bool MatchesSkillHeroScope(
        IReadOnlyCollection<CardHero> cardHeroes,
        CardHero hero
    ) => MatchesSkillHeroScope(cardHeroes, MaskOf(cardHeroes), hero);

    internal static int MaskOf(IReadOnlyCollection<CardHero> heroes)
    {
        var mask = 0;
        foreach (var hero in heroes)
            mask |= (uint)hero < 31u ? 1 << (int)hero : UnmappedBit;
        return mask;
    }

    private static bool MatchesSkillHeroScope(
        IReadOnlyCollection<CardHero> cardHeroes,
        int mask,
        CardHero hero
    )
    {
        var count = cardHeroes.Count;
        if (count == 1 && Contains(cardHeroes, mask, hero))
            return true;
        return count > 1
            && !Contains(cardHeroes, mask, CardHero.Common)
            && Contains(cardHeroes, mask, hero);
    }

    private static bool Contains(IReadOnlyCollection<CardHero> values, int mask, CardHero target)
    {
        if ((mask & UnmappedBit) == 0 && (uint)target < 31u)
            return (mask & (1 << (int)target)) != 0;

        foreach (var value in values)
        {
            if (value == target)
                return true;
        }
        return false;
    }
}
