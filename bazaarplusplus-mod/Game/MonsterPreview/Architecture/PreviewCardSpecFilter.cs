using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.PreviewSurface;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class PreviewCardSpecFilter
{
    internal static List<PreviewCardSpec> Filter(
        IEnumerable<PreviewCardSpec> specs,
        Func<Guid, bool> hasTemplate
    )
    {
        var filtered = new List<PreviewCardSpec>();
        if (specs == null || hasTemplate == null)
            return filtered;

        foreach (var spec in specs)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.TemplateId))
                continue;

            if (!Guid.TryParse(spec.TemplateId, out var templateId))
                continue;

            if (!hasTemplate(templateId))
                continue;

            filtered.Add(
                new PreviewCardSpec
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

        return filtered;
    }
}
