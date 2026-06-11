#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.Cards;

internal static class PackageIdentity
{
    public static bool IsPackage(IReadOnlyCollection<EHiddenTag>? hiddenTags)
    {
        if (hiddenTags == null)
            return false;

        foreach (var tag in hiddenTags)
        {
            if (tag == EHiddenTag.Package)
                return true;
        }

        return false;
    }
}
