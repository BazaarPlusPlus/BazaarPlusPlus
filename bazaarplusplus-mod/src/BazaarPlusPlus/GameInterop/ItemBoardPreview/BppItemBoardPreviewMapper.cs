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
                    EnchantmentType =
                        card.EnchantmentType is { } enchantment
                        && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var source)
                            ? source
                            : null,
                    Attributes = ToAttributes(card.Attributes),
                    InstanceIdPrefix = $"bpp-{board.Id.ToString().ToLowerInvariant()}",
                }
            );
        }

        return specs;
    }

    private static EContainerSocketId? ToSocket(int index) =>
        index >= 0 && index < SocketCount ? (EContainerSocketId)index : null;

    private static Dictionary<ECardAttributeType, int> ToAttributes(
        IReadOnlyDictionary<string, int>? attributes
    )
    {
        var result = new Dictionary<ECardAttributeType, int>();
        if (attributes == null)
            return result;

        foreach (var pair in attributes)
        {
            if (Enum.TryParse<ECardAttributeType>(pair.Key, ignoreCase: false, out var type))
                result[type] = pair.Value;
        }

        return result;
    }
}
