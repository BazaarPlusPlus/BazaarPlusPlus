#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewSubject
{
    public Guid TemplateId { get; init; }

    public ETier Tier { get; init; } = ETier.Bronze;

    public EContainerSocketId? SocketId { get; init; }

    public int DisplaySpan { get; init; } = 1;

    /// <summary>
    /// A board item's saved size, below its template's when Tiny. Read by the native board's
    /// <c>NativeMonsterBoardItemMapper</c>; the single-card factory shows the template's size.
    /// </summary>
    public ECardSize? Size { get; init; }

    public EEnchantmentType? EnchantmentType { get; init; }

    public IReadOnlyDictionary<ECardAttributeType, int>? Attributes { get; init; }

    public string InstanceIdPrefix { get; init; } = "bpp-card-preview";
}
