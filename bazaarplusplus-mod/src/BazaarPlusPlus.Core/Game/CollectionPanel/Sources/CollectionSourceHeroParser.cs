#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public enum CollectionSourceHeroParseStatus
{
    Resolved,
    KnownButUnavailable,
    Invalid,
}

public readonly record struct CollectionSourceHeroParseResult(
    CollectionSourceHeroParseStatus Status,
    CardHero? Hero
);

public static class CollectionSourceHeroParser
{
    // A hero BPP names but this game build lacks is KnownButUnavailable, never Invalid; both
    // The Dragons ids resolve to CardHero.TheDragons.
    public static CollectionSourceHeroParseResult Parse(
        string? value,
        Func<CardHero, bool> isAvailable
    )
    {
        if (isAvailable == null)
            throw new ArgumentNullException(nameof(isAvailable));
        if (string.IsNullOrWhiteSpace(value))
            return new CollectionSourceHeroParseResult(
                CollectionSourceHeroParseStatus.Invalid,
                null
            );

        var trimmed = value!.Trim();
        CardHero hero;
        if (TheDragonsHeroId.IsAlias(trimmed))
            hero = CardHero.TheDragons;
        else if (
            !Enum.TryParse(trimmed, ignoreCase: true, out hero)
            || !Enum.IsDefined(typeof(CardHero), hero)
        )
            return new CollectionSourceHeroParseResult(
                CollectionSourceHeroParseStatus.Invalid,
                null
            );

        return isAvailable(hero)
            ? new CollectionSourceHeroParseResult(CollectionSourceHeroParseStatus.Resolved, hero)
            : new CollectionSourceHeroParseResult(
                CollectionSourceHeroParseStatus.KnownButUnavailable,
                null
            );
    }
}
