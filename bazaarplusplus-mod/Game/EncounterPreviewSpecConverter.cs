#pragma warning disable CS0436
using System.Collections.Generic;
using BazaarPlusPlus.Game.MonsterPreview;

namespace BazaarPlusPlus;

internal static class EncounterPreviewSpecConverter
{
    internal static List<PreviewCardSpec> BuildCachedSpecs(List<RunInfo.MonsterPreviewCard> cards)
    {
        var specs = new List<PreviewCardSpec>();
        if (cards == null)
            return specs;

        foreach (var card in cards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.TemplateId))
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = card.TemplateId,
                    SourceName = card.SourceName ?? string.Empty,
                    Tier = card.Tier,
                    Size = card.Size <= 0 ? 1 : card.Size,
                    Enchant = string.IsNullOrWhiteSpace(card.Enchant) ? "None" : card.Enchant,
                    Attributes =
                        card.Attributes != null
                            ? new Dictionary<int, int>(card.Attributes)
                            : new Dictionary<int, int>(),
                }
            );
        }

        return specs;
    }

    internal static List<RunInfo.MonsterPreviewCard> ToCachedCards(
        IEnumerable<PreviewCardSpec> specs
    )
    {
        var cards = new List<RunInfo.MonsterPreviewCard>();
        if (specs == null)
            return cards;

        foreach (var spec in specs)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.TemplateId))
                continue;

            cards.Add(
                new RunInfo.MonsterPreviewCard
                {
                    TemplateId = spec.TemplateId,
                    SourceName = spec.SourceName ?? string.Empty,
                    Tier = spec.Tier,
                    Size = spec.Size <= 0 ? 1 : spec.Size,
                    Enchant = string.IsNullOrWhiteSpace(spec.Enchant) ? "None" : spec.Enchant,
                    Attributes =
                        spec.Attributes != null
                            ? new Dictionary<int, int>(spec.Attributes)
                            : new Dictionary<int, int>(),
                }
            );
        }

        return cards;
    }
}
