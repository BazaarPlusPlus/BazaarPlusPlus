#nullable enable
using System.Collections;

namespace BazaarPlusPlus.Core.GameState;

internal static class EncounterReflectionCollection
{
    internal static bool TryGetList(object? value, out IList list)
    {
        if (value is IList resolved)
        {
            list = resolved;
            return true;
        }

        list = null!;
        return false;
    }
}
