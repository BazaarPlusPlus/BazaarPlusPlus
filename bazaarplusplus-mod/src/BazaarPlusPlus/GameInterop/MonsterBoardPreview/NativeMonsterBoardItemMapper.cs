#nullable enable
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

// The native instance has no saved size. Carry the slot's size separately for Tiny items.
internal readonly record struct NativeBoardItem(TCardInstanceItem Instance, ECardSize? Size);

internal static class NativeMonsterBoardItemMapper
{
    internal static List<NativeBoardItem> Map(BppItemBoard board, string instanceIdPrefix) =>
        BppItemBoardPreviewMapper
            .Map(BppItemBoardSlotPlanner.Plan(board))
            .Where(card =>
                card.SocketId.HasValue
                && (int)card.SocketId.Value >= 0
                && (int)card.SocketId.Value + card.DisplaySpan <= 10
            )
            .Select(
                (card, index) =>
                    new NativeBoardItem(
                        new TCardInstanceItem
                        {
                            TemplateId = card.TemplateId,
                            TemplateVersion = string.Empty,
                            InstanceId = $"{instanceIdPrefix}-{index}",
                            Tier = card.Tier,
                            SocketId = card.SocketId,
                            EnchantmentType = card.EnchantmentType,
                            Attributes = card.Attributes == null ? new() : new(card.Attributes),
                        },
                        card.Size
                    )
            )
            .ToList();
}
