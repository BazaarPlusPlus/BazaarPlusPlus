#nullable enable
using BazaarGameShared.Domain.Cards.Item;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

internal static class NativeMonsterBoardItemMapper
{
    internal static List<TCardInstanceItem> Map(BppItemBoard board, string instanceIdPrefix) =>
        BppItemBoardPreviewMapper
            .Map(BppItemBoardSlotPlanner.Plan(board))
            .Where(card =>
                card.SocketId.HasValue
                && (int)card.SocketId.Value >= 0
                && (int)card.SocketId.Value + card.DisplaySpan <= 10
            )
            .Select(
                (card, index) =>
                    new TCardInstanceItem
                    {
                        TemplateId = card.TemplateId,
                        TemplateVersion = string.Empty,
                        InstanceId = $"{instanceIdPrefix}-{index}",
                        Tier = card.Tier,
                        SocketId = card.SocketId,
                        EnchantmentType = card.EnchantmentType,
                        Attributes = card.Attributes == null ? new() : new(card.Attributes),
                    }
            )
            .ToList();
}
