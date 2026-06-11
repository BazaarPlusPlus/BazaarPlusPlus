#nullable enable

namespace BazaarPlusPlus.Core.GameState;

internal interface IEncounterStateProbe
{
    /// <summary>Main thread only. Lightweight read for current and choice-screen
    /// encounter ids. Safe for high-frequency UI paths.</summary>
    EncounterIdsSnapshot GetEncounterIds();

    /// <summary>Main thread only. Resolves the currently offered choice-screen
    /// pedestal kind from the lightweight id snapshot.</summary>
    ChoicePedestalSnapshot GetChoicePedestal();

    /// <summary>Main thread only. Reads target-selection state. This may use
    /// reflection and pedestal validation, so callers should only use it when they
    /// need action legality.</summary>
    EncounterTargetingSnapshot GetTargetingState();
}
