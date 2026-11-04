#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.Cards;

/// <summary>
/// A saved card's attribute values, keyed by the game's member name, written over a card's
/// attributes; a name the game does not know is dropped (ADR-0017). The game's preview set-up
/// writes the template's tier values over any instance value, so these go on after it.
/// </summary>
internal static class SavedCardValues
{
    public static void Write(
        IDictionary<ECardAttributeType, int> target,
        IReadOnlyDictionary<string, int>? saved
    )
    {
        if (saved == null)
            return;
        foreach (var pair in saved)
        {
            if (Enum.TryParse<ECardAttributeType>(pair.Key, false, out var attribute))
                target[attribute] = pair.Value;
        }
    }
}
