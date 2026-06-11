#nullable enable

using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class BppItemBoardCard
{
    public Guid TemplateId { get; init; }

    public string InstanceId { get; init; } = string.Empty;

    public int Order { get; init; }

    public ETier Tier { get; init; } = ETier.Bronze;

    public ECardSize Size { get; init; } = ECardSize.Small;

    public int Span { get; init; }

    public EEnchantmentType? EnchantmentType { get; init; }

    public IReadOnlyDictionary<ECardAttributeType, int> Attributes { get; init; } =
        new Dictionary<ECardAttributeType, int>();

    public EContainerSocketId? SourceSocketId { get; init; }

    public EContainerSocketId? DisplaySocketId { get; init; }

    public int DisplaySpan => BppItemBoardSpan.Resolve(Size, Span);

    public BppItemBoardCard WithDisplaySocket(EContainerSocketId displaySocketId) =>
        new()
        {
            TemplateId = TemplateId,
            InstanceId = InstanceId,
            Order = Order,
            Tier = Tier,
            Size = Size,
            Span = Span,
            EnchantmentType = EnchantmentType,
            Attributes =
                Attributes != null
                    ? new Dictionary<ECardAttributeType, int>(Attributes)
                    : new Dictionary<ECardAttributeType, int>(),
            SourceSocketId = SourceSocketId,
            DisplaySocketId = displaySocketId,
        };
}
