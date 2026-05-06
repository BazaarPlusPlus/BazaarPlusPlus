#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.ItemBoard;

internal sealed class ItemBoardItemSpec
{
    public Guid TemplateId { get; set; }

    public ETier Tier { get; set; } = ETier.Bronze;

    public EEnchantmentType? EnchantmentType { get; set; }

    public EContainerSocketId? SocketId { get; set; }

    public IReadOnlyDictionary<ECardAttributeType, int> Attributes { get; set; } =
        new Dictionary<ECardAttributeType, int>();

    public ItemBoardItemSpec Clone()
    {
        return new ItemBoardItemSpec
        {
            TemplateId = TemplateId,
            Tier = Tier,
            EnchantmentType = EnchantmentType,
            SocketId = SocketId,
            Attributes =
                Attributes != null
                    ? new Dictionary<ECardAttributeType, int>(Attributes)
                    : new Dictionary<ECardAttributeType, int>(),
        };
    }
}
