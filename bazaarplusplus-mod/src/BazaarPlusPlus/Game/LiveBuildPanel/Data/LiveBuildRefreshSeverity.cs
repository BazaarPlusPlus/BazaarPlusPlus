#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

// Drives the colour of the manual ten-win refresh status label in the rail. Deliberately local to
// LiveBuildPanel: the HistoryPanel StatusSeverity enum lives behind the feature boundary that the
// architecture tests forbid importing across.
internal enum LiveBuildRefreshSeverity
{
    Neutral,
    Pending,
    Success,
    Failure,
}
