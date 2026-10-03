#nullable enable

namespace BazaarPlusPlus.Core.GameState;

internal interface IEncounterStateProbe
{
    /// <summary>Main thread only. Lightweight read for current and choice-screen
    /// encounter ids. Safe for high-frequency UI paths. A failed read carries its reason, so a
    /// fallback empty snapshot cannot be mistaken for a complete selection read.</summary>
    EncounterIdsProbeOutcome GetEncounterIdsOutcome();

    /// <summary>Main thread only. Resolves the currently offered choice-screen
    /// pedestal kind from the lightweight id snapshot.</summary>
    ChoicePedestalProbeOutcome GetChoicePedestalOutcome();
}
