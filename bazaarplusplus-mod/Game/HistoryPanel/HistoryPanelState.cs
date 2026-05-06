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

    public bool DeleteRunConfirmationStatusActive { get; set; }

    public PreviewSelectionMode PreviewSelectionMode { get; set; } = PreviewSelectionMode.Run;

    public HistorySectionMode SectionMode { get; set; } = HistorySectionMode.Runs;

    public bool GhostSyncInProgress { get; set; }

    public bool FinalBuildRefreshInProgress { get; set; }

    public bool ReplayActionInProgress { get; set; }

    public bool FilteredGhostBattlesDirty { get; set; } = true;

    public bool IsVisible { get; set; }

    public bool ShouldClearStatusWhenDeleteConfirmationExpires()
    {
        return DeleteRunConfirmationStatusActive;
    }

    public HistoryRunRecord? GetSelectedRun() => SafeIndex(Runs, SelectedRunIndex);

    public HistoryBattleRecord? GetSelectedBattle() => SafeIndex(Battles, SelectedBattleIndex);

    public HistoryBattleRecord? GetSelectedGhostBattle(
        IReadOnlyList<HistoryBattleRecord> filteredGhostBattles
    ) => SafeIndex(filteredGhostBattles, SelectedGhostBattleIndex);

    private static T? SafeIndex<T>(IReadOnlyList<T> list, int index)
        where T : class
    {
        if (list.Count == 0)
            return null;

        if (index < 0)
            return list[0];
        if (index >= list.Count)
            return list[list.Count - 1];
        return list[index];
    }
}
