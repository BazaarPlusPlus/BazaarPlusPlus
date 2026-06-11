#nullable enable

using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.LiveCards;

internal sealed class LiveCardSnapshot
{
    public string InstanceId { get; init; } = string.Empty;

    public Guid TemplateId { get; init; }

    public int Order { get; init; }

    public ETier Tier { get; init; } = ETier.Bronze;

    public ECardSize Size { get; init; } = ECardSize.Small;

    public EEnchantmentType? EnchantmentType { get; init; }

    public EContainerSocketId? SocketId { get; init; }

    public IReadOnlyDictionary<ECardAttributeType, int> Attributes { get; init; } =
        new Dictionary<ECardAttributeType, int>();
}
