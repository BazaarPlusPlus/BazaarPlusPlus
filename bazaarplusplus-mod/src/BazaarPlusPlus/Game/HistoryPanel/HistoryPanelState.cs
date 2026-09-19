#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel;

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
    public IReadOnlyList<HistoryRunRecord> Runs => RunPage.Rows;

    public IReadOnlyList<HistoryBattleRecord> Battles => BattlePage.Rows;

    public IReadOnlyList<HistoryBattleRecord> GhostBattles => GhostPage.Rows;

    public HistoryPage<HistoryRunRecord> RunPage { get; set; } =
        HistoryPage<HistoryRunRecord>.Empty;
    public HistoryPage<HistoryBattleRecord> BattlePage { get; set; } =
        HistoryPage<HistoryBattleRecord>.Empty;
    public HistoryPage<HistoryBattleRecord> GhostPage { get; set; } =
        HistoryPage<HistoryBattleRecord>.Empty;
    public PvpBattleSnapshots? DetailSnapshots { get; set; }
    public string? DetailBattleId { get; set; }
    public bool DetailLoading { get; set; }
    public bool DetailFailed { get; set; }
    public bool PageLoading { get; set; }

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public int SelectedGhostBattleIndex { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; } = GhostBattleFilter.All;

    public string? SelectedRunHero { get; set; }

    public bool GhostDayMin10 { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    public DeleteConfirmation DeleteRunConfirmation { get; set; }

    public bool DeleteRunConfirmationStatusActive { get; set; }

    public HistorySectionMode SectionMode { get; set; } = HistorySectionMode.Runs;

    public bool GhostSyncInProgress { get; set; }

    public bool ReplayActionInProgress { get; set; }

    public bool ServerHealthProbeInProgress { get; set; }

    public bool AccountLinkInProgress { get; set; }

    public string? CachedAccountId { get; set; }

    public bool LocalLinkedHint { get; set; }

    // Pure UI disclosure flag; panels open with account linking collapsed by default.
    public bool AccountLinkExpanded { get; set; }

    public string? AccountLinkBannerMessage { get; set; }

    public StatusSeverity AccountLinkBannerSeverity { get; set; }

    public bool ShouldClearStatusWhenDeleteConfirmationExpires()
    {
        return DeleteRunConfirmationStatusActive;
    }

    public HistoryRunRecord? GetSelectedRun(IReadOnlyList<HistoryRunRecord> filteredRuns) =>
        SafeIndex(filteredRuns, SelectedRunIndex);

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
