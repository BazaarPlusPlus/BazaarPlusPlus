#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.Cards;

// Its own file, so a test that maps boards links it without the card prefab types.
internal static partial class NativeCardSetUp
{
    /// <summary>
    /// Saved Card Values, keyed by the game's member name, as the game's attributes; a name the
    /// game does not know is dropped (ADR-0017). Empty when there are none.
    /// </summary>
    public static Dictionary<ECardAttributeType, int> Values(
        IReadOnlyDictionary<string, int>? saved
    )
    {
        var values = new Dictionary<ECardAttributeType, int>();
        if (saved == null)
            return values;
        foreach (var pair in saved)
        {
            if (Enum.TryParse<ECardAttributeType>(pair.Key, false, out var attribute))
                values[attribute] = pair.Value;
        }
        return values;
    }
}
