#nullable enable

using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CardPreview;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class BppItemBoardPreviewMapper
{
    public static IReadOnlyList<NativeCardPreviewSpec> Map(BppItemBoard? board)
    {
        if (board == null || board.Cards.Count == 0)
            return Array.Empty<NativeCardPreviewSpec>();

        var specs = new List<NativeCardPreviewSpec>(board.Cards.Count);
        foreach (var card in board.Cards)
        {
            if (card == null || card.TemplateId == Guid.Empty)
                continue;

            specs.Add(
                new NativeCardPreviewSpec
                {
                    TemplateId = card.TemplateId,
                    Tier = card.Tier,
                    SocketId =
                        card.DisplaySocketId ?? card.SourceSocketId ?? EContainerSocketId.Socket_0,
                    DisplaySpan = card.DisplaySpan,
                    EnchantmentType = card.EnchantmentType,
                    Attributes = card.Attributes,
                    InstanceIdPrefix = $"bpp-{board.Id.ToString().ToLowerInvariant()}",
                }
            );
        }

        return specs;
    }
}
