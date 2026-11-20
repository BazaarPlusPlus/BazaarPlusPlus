#nullable enable

namespace BazaarPlusPlus.Core.Cards;

// The native bar comes from Combat Recap, whose placement is larger and lower than the board's.
// These measurements reproduce the board placement and keep the pips on the frame's bottom border.
public static class AmmoBarPlacement
{
    public const float CardHeightUnits = 330f;

    public const float CentreBelowUnits = 141f;

    public static float Scale(float cardHeight) => cardHeight / CardHeightUnits;

    public static float CentreY(float cardHeight) => -CentreBelowUnits * Scale(cardHeight);
}
