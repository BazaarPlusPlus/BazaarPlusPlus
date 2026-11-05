#nullable enable

using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

/// <summary>
/// Maps a Core item board back to the game's types for native card previews: facets through
/// <see cref="DesktopCardFacets"/>, socket indexes to <see cref="EContainerSocketId"/>, and
/// attribute member names to <see cref="ECardAttributeType"/> (ADR-0017).
/// </summary>
internal static class BppItemBoardPreviewMapper
{
    private const int SocketCount = 10;

    public static IReadOnlyList<NativeCardPreviewSubject> Map(BppItemBoard? board)
    {
        if (board == null || board.Cards.Count == 0)
            return Array.Empty<NativeCardPreviewSubject>();

        var specs = new List<NativeCardPreviewSubject>(board.Cards.Count);
        foreach (var card in board.Cards)
        {
            if (card == null || card.TemplateId == Guid.Empty)
                continue;

            specs.Add(
                new NativeCardPreviewSubject
                {
                    TemplateId = card.TemplateId,
                    Tier = DesktopCardFacets.Tier.TryToSource(card.Tier, out var tier)
                        ? tier
                        : ETier.Bronze,
                    SocketId = ToSocket(card.DisplaySocketId ?? card.SourceSocketId ?? 0),
                    DisplaySpan = card.DisplaySpan,
                    Size = DesktopCardFacets.Size.TryToSource(card.Size, out var size)
                        ? size
                        : null,
                    EnchantmentType =
                        card.EnchantmentType is { } enchantment
                        && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var source)
                            ? source
                            : null,
                    Attributes = NativeCardSetUp.Values(card.Attributes),
                    InstanceIdPrefix = $"bpp-{board.Id.ToString().ToLowerInvariant()}",
                }
            );
        }

        return specs;
    }

    private static EContainerSocketId? ToSocket(int index) =>
        index >= 0 && index < SocketCount ? (EContainerSocketId)index : null;
}
