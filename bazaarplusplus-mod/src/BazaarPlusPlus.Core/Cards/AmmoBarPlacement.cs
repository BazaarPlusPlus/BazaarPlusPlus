#nullable enable

namespace BazaarPlusPlus.Core.Cards;

/// <summary>
/// Where an item's ammo bar sits on a native card that BPP draws on an item board, and how large
/// it is. The bar is the game's own, borrowed from the combat recap card, and it draws in its own
/// units: a pip is 20 wide. The values match the bar on the game's board, measured beside a BPP
/// board in game 1.0.13176. Its pips sit on the frame's bottom border, and a card is
/// <see cref="CardHeightUnits"/> of those units high. The recap card's own layout
/// (a 294-unit canvas, the bar 154 below its centre) draws the bar larger and lower than the
/// board does. A host scales the bar to its card's height (<see cref="NativeCardPrefab.Height"/>).
/// </summary>
public static class AmmoBarPlacement
{
    /// <summary>A card's height, in the units the bar draws in.</summary>
    public const float CardHeightUnits = 330f;

    /// <summary>How far the bar's centre is below the card's centre, in the bar's units.</summary>
    public const float CentreBelowUnits = 141f;

    /// <summary>The bar's scale on a card that is <paramref name="cardHeight"/> high.</summary>
    public static float Scale(float cardHeight) => cardHeight / CardHeightUnits;

    /// <summary>The bar centre's offset from the card's centre; negative is below.</summary>
    public static float CentreY(float cardHeight) => -CentreBelowUnits * Scale(cardHeight);
}
