#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionTabKind
{
    Items,
    Skills,
}

internal static class CollectionTabKindExtensions
{
    public static ECardType CardType(this CollectionTabKind tab) =>
        tab == CollectionTabKind.Skills ? ECardType.Skill : ECardType.Item;
}
