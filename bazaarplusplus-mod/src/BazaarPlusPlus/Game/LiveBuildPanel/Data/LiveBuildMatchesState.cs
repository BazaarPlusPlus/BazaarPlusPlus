#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

// Selects what the Matches card body shows: a single guidance line (no run / no candidates / no
// matching build) or the detailed per-recommendation stat rows (has recommendation).
internal enum LiveBuildMatchesState
{
    NoRun,
    NoCandidates,
    NoRecommendation,
    HasRecommendation,
}
