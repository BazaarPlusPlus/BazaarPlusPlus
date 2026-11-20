#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewSubject
{
    public Guid TemplateId { get; init; }

    public ETier Tier { get; init; } = ETier.Bronze;

    public EContainerSocketId? SocketId { get; init; }

    public int DisplaySpan { get; init; } = 1;

    // The native board uses the saved size, while the single-card factory uses the template size.
    public ECardSize? Size { get; init; }

    public EEnchantmentType? EnchantmentType { get; init; }

    public IReadOnlyDictionary<ECardAttributeType, int>? Attributes { get; init; }

    public string InstanceIdPrefix { get; init; } = "bpp-card-preview";
}
