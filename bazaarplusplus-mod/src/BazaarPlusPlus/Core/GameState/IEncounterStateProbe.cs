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
}

/// <summary>
/// Optional diagnostic-preserving encounter-ID read. Existing callers keep using
/// <see cref="IEncounterStateProbe.GetEncounterIds"/>; operational owners opt into this seam so
/// a fallback empty snapshot cannot be mistaken for a complete selection read.
/// </summary>
internal interface ITypedEncounterIdsProbe
{
    EncounterIdsProbeOutcome GetEncounterIdsOutcome();
}

internal interface ITypedEncounterStateProbe : ITypedEncounterIdsProbe
{
    ChoicePedestalProbeOutcome GetChoicePedestalOutcome();
}
