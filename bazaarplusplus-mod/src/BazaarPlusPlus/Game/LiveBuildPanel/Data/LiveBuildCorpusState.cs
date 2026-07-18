#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

// Selects what the corpus card body shows: a single status line (pending / failure / empty) or the
// per-hero dashboard (summary). The card is fixed-height, so the view only toggles which child is
// displayed and never reflows the rail.
internal enum LiveBuildCorpusState
{
    Pending,
    Failure,
    Empty,
    Summary,
}
