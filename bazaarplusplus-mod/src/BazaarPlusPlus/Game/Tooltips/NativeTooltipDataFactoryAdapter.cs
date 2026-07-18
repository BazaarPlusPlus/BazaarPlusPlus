#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.GameInterop.CardPreview;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class NativeTooltipDataFactoryAdapter : INativeTooltipDataFactory
{
    public CardTooltipData Create(
        Card card,
        CardTooltipData source,
        NativeTooltipRefreshMode mode
    ) =>
        CardTooltipDataFactory.Create(
            card,
            source,
            mode switch
            {
                NativeTooltipRefreshMode.Enchant => TooltipPreviewRefreshMode.Enchant,
                NativeTooltipRefreshMode.Upgrade => TooltipPreviewRefreshMode.Upgrade,
                _ => TooltipPreviewRefreshMode.Normal,
            }
        );
}
