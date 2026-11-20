#nullable enable

namespace BazaarPlusPlus.Core.GameState;

public interface IEncounterStateProbe
{
    // Main thread only. Callers must distinguish a failed read from a complete empty selection.
    EncounterIdsProbeOutcome GetEncounterIdsOutcome();

    // Main thread only.
    ChoicePedestalProbeOutcome GetChoicePedestalOutcome();
}
