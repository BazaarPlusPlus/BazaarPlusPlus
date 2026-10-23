#nullable enable

namespace BazaarPlusPlus.Core.Cards.ItemBoards;

public static class BppItemBoardSpan
{
    public static int Resolve(CardSize size, int explicitSpan = 0)
    {
        if (explicitSpan > 0)
            return explicitSpan;

        return size switch
        {
            CardSize.Small => 1,
            CardSize.Medium => 2,
            CardSize.Large => 3,
            _ => 1,
        };
    }
}
