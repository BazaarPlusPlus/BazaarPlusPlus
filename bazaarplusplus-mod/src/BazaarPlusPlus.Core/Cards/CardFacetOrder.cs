#nullable enable
namespace BazaarPlusPlus.Core.Cards;

/// <summary>The visible orders of card facets, independent of any enum's integer values.</summary>
public static class CardFacetOrder
{
    // Canonical ordering for card tags that are meaningful to players. Feature-specific
    // consumers may filter this list further (aggregate item effects drop Merchant, which item
    // templates never carry).
    public static IReadOnlyList<CardTag> PlayerFacingTags { get; } =
        Array.AsReadOnly(
            new[]
            {
                CardTag.Weapon,
                CardTag.Friend,
                CardTag.Aquatic,
                CardTag.Tool,
                CardTag.Vehicle,
                CardTag.Food,
                CardTag.Trap,
                CardTag.Toy,
                CardTag.Potion,
                CardTag.Reagent,
                CardTag.Relic,
                CardTag.Dragon,
                CardTag.Core,
                CardTag.Tech,
                CardTag.Dinosaur,
                CardTag.Apparel,
                CardTag.Merchant,
                CardTag.Property,
                CardTag.Loot,
                CardTag.Instrument,
                CardTag.Drone,
                CardTag.Ray,
            }
        );

    public static int TierRank(CardTier tier) =>
        tier switch
        {
            CardTier.Bronze => 0,
            CardTier.Silver => 1,
            CardTier.Gold => 2,
            CardTier.Diamond => 3,
            CardTier.Legendary => 4,
            _ => 99,
        };

    public static int SizeRank(CardSize size) =>
        size switch
        {
            CardSize.Small => 0,
            CardSize.Medium => 1,
            CardSize.Large => 2,
            _ => 99,
        };

    // Board order, left to right: Socket_0 first.
    public static int SocketRank(CardSocket socket) =>
        socket switch
        {
            CardSocket.Socket_0 => 0,
            CardSocket.Socket_1 => 1,
            CardSocket.Socket_2 => 2,
            CardSocket.Socket_3 => 3,
            CardSocket.Socket_4 => 4,
            CardSocket.Socket_5 => 5,
            CardSocket.Socket_6 => 6,
            CardSocket.Socket_7 => 7,
            CardSocket.Socket_8 => 8,
            CardSocket.Socket_9 => 9,
            _ => 99,
        };

    // The inverse of SocketRank: the socket at a board index 0..9, otherwise none.
    public static CardSocket? SocketAt(int index) =>
        index switch
        {
            0 => CardSocket.Socket_0,
            1 => CardSocket.Socket_1,
            2 => CardSocket.Socket_2,
            3 => CardSocket.Socket_3,
            4 => CardSocket.Socket_4,
            5 => CardSocket.Socket_5,
            6 => CardSocket.Socket_6,
            7 => CardSocket.Socket_7,
            8 => CardSocket.Socket_8,
            9 => CardSocket.Socket_9,
            _ => null,
        };
}
