#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewSpec
{
    public Guid TemplateId { get; init; }

    public ETier Tier { get; init; } = ETier.Bronze;

    public EContainerSocketId? SocketId { get; init; }

    public int DisplaySpan { get; init; } = 1;

    public EEnchantmentType? EnchantmentType { get; init; }

    public IReadOnlyDictionary<ECardAttributeType, int>? Attributes { get; init; }

    public string InstanceIdPrefix { get; init; } = "bpp-card-preview";
}
