#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public enum CollectionTabKind
{
    Items,
    Skills,
}

public static class CollectionTabKindExtensions
{
    public static CardType CardType(this CollectionTabKind tab) =>
        tab == CollectionTabKind.Skills
            ? BazaarPlusPlus.Core.Cards.CardType.Skill
            : BazaarPlusPlus.Core.Cards.CardType.Item;
}
