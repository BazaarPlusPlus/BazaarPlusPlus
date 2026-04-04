#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum PreviewSelectionMode
{
    Run,
    Battle,
}

internal enum HistorySectionMode
{
    Runs,
    Ghost,
}

internal enum GhostBattleFilter
{
    All,
    IWon,
    ILost,
}

internal sealed class HistoryPanelState
{
    public List<HistoryRunRecord> Runs { get; } = new();

    public List<HistoryBattleRecord> Battles { get; } = new();

    public List<HistoryBattleRecord> GhostBattles { get; } = new();

    public List<HistoryBattleRecord> FilteredGhostBattles { get; } = new();

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public int SelectedGhostBattleIndex { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; } = GhostBattleFilter.All;

    public string? StatusMessage { get; set; }

    public string? DeleteRunConfirmationRunId { get; set; }

    public float DeleteRunConfirmationUntil { get; set; }

    public PreviewSelectionMode PreviewSelectionMode { get; set; } = PreviewSelectionMode.Run;

    public HistorySectionMode SectionMode { get; set; } = HistorySectionMode.Runs;

    public bool GhostSyncInProgress { get; set; }

    public bool ReplayActionInProgress { get; set; }

    public bool FilteredGhostBattlesDirty { get; set; } = true;

    public bool IsVisible { get; set; }

    public bool ShouldClearStatusWhenDeleteConfirmationExpires()
    {
        return StatusMessage != null
            && StatusMessage.StartsWith(
                "Click Delete Run again within 5s to remove ",
                StringComparison.Ordinal
            );
    }
}
