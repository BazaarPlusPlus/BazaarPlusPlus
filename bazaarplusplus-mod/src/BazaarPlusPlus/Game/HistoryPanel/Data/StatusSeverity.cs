#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

// Drives the categorized status-banner / detail-pill visual language. Phase 1 emits only
// Confirm/Pending/Neutral (derived from existing HistoryPanelState flags); Success/Failure are
// reserved for the phase-2 increment that threads a severity arg through the relevant
// success/failure SetStatusMessage call sites.
internal enum StatusSeverity
{
    Neutral,
    Pending,
    Success,
    Failure,
    Confirm,
}
