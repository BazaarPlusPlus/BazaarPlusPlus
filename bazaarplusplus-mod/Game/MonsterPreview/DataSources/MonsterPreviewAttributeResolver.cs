#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewAttributeResolver
{
    public static Dictionary<int, int> Build(Guid templateId, string tier)
    {
        var result = new Dictionary<int, int>();
        foreach (var pair in ItemAttr.GetAttributes(templateId, tier))
        {
            if (!Enum.TryParse<ECardAttributeType>(pair.Key, out var attributeType))
                continue;

            result[(int)attributeType] = pair.Value;
        }

        return result;
    }
}
