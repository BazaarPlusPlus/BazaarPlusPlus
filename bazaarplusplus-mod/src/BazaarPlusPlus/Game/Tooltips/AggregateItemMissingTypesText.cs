#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CardTags;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class AggregateItemMissingTypesText
{
    private static readonly LocalizedTextSet Heading = new(
        "Missing Types:",
        "尚缺类型：",
        "尚缺類型："
    );

    // Aggregate item auras copy item types only; Merchant is a player-facing card
    // tag used by collection data but is not a type carried by item templates.
    internal static readonly IReadOnlyList<ECardTag> ItemTypes = PlayerFacingCardTags
        .Ordered.Where(tag => tag != ECardTag.Merchant)
        .ToArray();

    internal static IReadOnlyList<ECardTag> FindMissing(IEnumerable<ECardTag> present)
    {
        var presentSet = new HashSet<ECardTag>(present ?? Array.Empty<ECardTag>());
        return ItemTypes.Where(tag => !presentSet.Contains(tag)).ToArray();
    }

    internal static string? Build(
        IEnumerable<ECardTag> present,
        Func<string, string>? colorize = null,
        Func<string, string?>? localizeType = null
    )
    {
        var missing = FindMissing(present);
        if (missing.Count == 0)
            return null;

        var typeList = string.Join(
            ", ",
            missing.Select(tag =>
            {
                var canonicalName = tag.ToString();
                return localizeType?.Invoke(canonicalName) ?? canonicalName;
            })
        );
        var content = $"{L.Resolve(Heading)} {typeList}";
        return colorize?.Invoke(content) ?? content;
    }
}
