#pragma warning disable CS0436
using System.Linq;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class PreviewBoardSignature
{
    public static string Build(PreviewBoardModel model)
    {
        if (model == null)
            return string.Empty;

        var itemCards = string.Join("|", model.ItemCards.Select(BuildCardSignature));
        var skillCards = string.Join("|", model.SkillCards.Select(BuildCardSignature));
        var metadata = string.Join(
            "|",
            model.Metadata.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}")
        );

        return string.Join("::", model.Title ?? string.Empty, itemCards, skillCards, metadata);
    }

    private static string BuildCardSignature(PreviewCardSpec card)
    {
        if (card == null)
            return string.Empty;

        var attributes = string.Join(
            ",",
            card.Attributes.OrderBy(entry => entry.Key)
                .Select(entry => $"{entry.Key}:{entry.Value}")
        );

        return string.Join(
            ";",
            card.TemplateId ?? string.Empty,
            card.SourceName ?? string.Empty,
            card.Tier.ToString(),
            card.Size.ToString(),
            card.Enchant ?? "None",
            attributes
        );
    }
}
