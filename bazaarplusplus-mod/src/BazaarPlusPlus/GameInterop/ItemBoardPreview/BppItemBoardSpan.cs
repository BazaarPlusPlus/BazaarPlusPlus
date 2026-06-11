#nullable enable

using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class BppItemBoardSpan
{
    public static int Resolve(ECardSize size, int explicitSpan = 0)
    {
        if (explicitSpan > 0)
            return explicitSpan;

        return CardSizeSpan.Resolve(size);
    }
}
