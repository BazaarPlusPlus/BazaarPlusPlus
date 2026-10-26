#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;

namespace BazaarPlusPlus.Game.HistoryPanel;

public enum HistorySectionMode
{
    Runs,
    Ghost,
    Favorites,
}

// TrySyncGhostBattlesAsync alone writes Running, Failed, and Completed; each session boundary
// (OnPanelHidden, ObserveAccount) writes NotStarted, because a sync its session cancelled returns
// without touching state and would otherwise stay Running.
public enum GhostSyncPhase
{
    NotStarted,
    Running,
    Failed,
    Completed,
}

public enum GhostBattleFilter
{
    All,
    IWon,
    ILost,
}

public sealed class HistoryPanelState
{
    public IReadOnlyList<HistoryRunRecord> Runs => RunPage.Rows;

    public IReadOnlyList<HistoryBattleRecord> Battles => BattlePage.Rows;

    public IReadOnlyList<HistoryBattleRecord> GhostBattles => GhostPage.Rows;

    public IReadOnlyList<HistoryFavoriteRecord> Favorites => FavoritePage.Rows;

    public HistoryCountedPage<HistoryRunRecord> RunPage { get; set; } =
        HistoryCountedPage<HistoryRunRecord>.Empty();
    public HistoryCursorPage<HistoryBattleRecord> BattlePage { get; set; } =
        HistoryCursorPage<HistoryBattleRecord>.Empty;
    public HistoryCountedPage<HistoryBattleRecord> GhostPage { get; set; } =
        HistoryCountedPage<HistoryBattleRecord>.Empty();
    public HistoryCountedPage<HistoryFavoriteRecord> FavoritePage { get; set; } =
        HistoryCountedPage<HistoryFavoriteRecord>.Empty();

    // The detail read for DetailBattleId returned snapshots; the host keeps the snapshots.
    public bool DetailLoaded { get; set; }
    public string? DetailBattleId { get; set; }
    public bool DetailLoading { get; set; }
    public bool DetailFailed { get; set; }
    public bool PageLoading { get; set; }
    public bool PageLoadFailed { get; set; }

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public int SelectedGhostBattleIndex { get; set; }

    public int SelectedFavoriteIndex { get; set; }

    // Whether the battle the detail read last checked (Runs or Ghost) is already a favorite.
    public string? FavoriteCheckBattleId { get; set; }
    public bool FavoriteCheckIsFavorite { get; set; }
    public bool FavoriteActionInProgress { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; } = GhostBattleFilter.All;

    public string? SelectedRunHero { get; set; }

    public bool GhostDayMin10 { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    // Keyed by run id in Runs and by battle id in Favorites, where the delete button removes the
    // favorite; every section change and archive load clears it.
    public DeleteConfirmation DeleteRunConfirmation { get; set; }

    public bool DeleteRunConfirmationStatusActive { get; set; }

    // A confirmed run delete is running off the main thread; Delete stays disabled until it ends.
    public bool RunDeleteInProgress { get; set; }

    public HistorySectionMode SectionMode { get; set; } = HistorySectionMode.Runs;

    public GhostSyncPhase GhostSync { get; set; }

    public bool ReplayActionInProgress { get; set; }
    public string? ReplayActionBattleId { get; set; }
    public string? ReplayFailureMessage { get; set; }

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

    public HistoryFavoriteRecord? GetSelectedFavorite() =>
        SafeIndex(Favorites, SelectedFavoriteIndex);

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
