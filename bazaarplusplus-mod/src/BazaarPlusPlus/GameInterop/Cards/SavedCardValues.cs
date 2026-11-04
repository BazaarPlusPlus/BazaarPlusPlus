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

    /// <summary>
    /// Runs a preview's set-up for an instance that carries typed saved values, then writes them
    /// over the card's attributes. The set-up adopts the instance's dictionary and writes the
    /// starting tier into it, so the values are copied first and restored after, which keeps the
    /// instance right for a later render. Returns whether anything was written.
    /// </summary>
    public static async Task<bool> KeepThroughSetUp(
        Dictionary<ECardAttributeType, int>? instanceAttributes,
        Func<Task> setUp,
        Func<IDictionary<ECardAttributeType, int>?> attributesAfterSetUp
    )
    {
        if (instanceAttributes is not { Count: > 0 })
        {
            await setUp();
            return false;
        }
        var saved = new Dictionary<ECardAttributeType, int>(instanceAttributes);
        await setUp();
        instanceAttributes.Clear();
        foreach (var pair in saved)
            instanceAttributes[pair.Key] = pair.Value;
        if (attributesAfterSetUp() is not { } target)
            return false;
        foreach (var pair in saved)
            target[pair.Key] = pair.Value;
        return true;
    }
}
