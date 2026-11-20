#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

public enum HistoryDetailPhase
{
    NotSelectedBattle,

    Loading,
    Missing,
    Loaded,
}

public enum HistoryPageLabelMode
{
    Loading,
    Totals,
    Hidden,
}

public readonly record struct HistoryBoardFacts(NativeMonsterBoardStatus? Status, int PartialCount);

public readonly record struct HistorySelectedGhostReplay(
    ReplayAvailability Availability,
    bool ActionInProgress,
    string? FailureMessage
);

public sealed record HistoryArchiveFacts(
    HistorySectionMode Section,
    bool AccountKnown,
    bool PageLoading,
    bool PageLoadFailed,
    GhostSyncPhase GhostSync,
    bool FiltersActive,
    int RowCount,
    int BattleCount,
    HistorySelectedGhostReplay? SelectedGhostReplay,
    HistoryDetailPhase Detail,
    bool DetailFailed,
    HistoryBoardFacts PlayerBoard,
    HistoryBoardFacts OpponentBoard
)
{
    public static HistoryArchiveFacts Observe(
        HistoryPanelState state,
        HistoryBattleRecord? selected,
        HistoryBoardFacts playerBoard,
        HistoryBoardFacts opponentBoard
    )
    {
        var ghost = state.SectionMode == HistorySectionMode.Ghost;
        var ownsReplayAction = selected != null && state.ReplayActionBattleId == selected.BattleId;
        return new(
            state.SectionMode,
            !string.IsNullOrWhiteSpace(state.CachedAccountId),
            state.PageLoading,
            state.PageLoadFailed,
            state.GhostSync,
            state.SectionMode switch
            {
                HistorySectionMode.Ghost => state.GhostBattleFilter != GhostBattleFilter.All
                    || state.GhostDayMin10,
                HistorySectionMode.Runs => !string.IsNullOrEmpty(state.SelectedRunHero),
                _ => false,
            },
            state.SectionMode switch
            {
                HistorySectionMode.Ghost => state.GhostBattles.Count,
                HistorySectionMode.Favorites => state.Favorites.Count,
                _ => state.Runs.Count,
            },
            state.Battles.Count,
            ghost && selected is { Source: HistoryBattleSource.Ghost }
                ? new HistorySelectedGhostReplay(
                    selected.Replay,
                    ownsReplayAction && state.ReplayActionInProgress,
                    ownsReplayAction ? state.ReplayFailureMessage : null
                )
                : null,
            state.DetailLoading ? HistoryDetailPhase.Loading
                : selected == null || state.DetailBattleId != selected.BattleId
                    ? HistoryDetailPhase.NotSelectedBattle
                : !state.DetailLoaded ? HistoryDetailPhase.Missing
                : HistoryDetailPhase.Loaded,
            state.DetailFailed,
            playerBoard,
            opponentBoard
        );
    }
}

public readonly record struct HistoryArchiveStatus(
    string EmptyMessage,
    string PlayerBoardMessage,
    string OpponentBoardMessage,
    HistoryPageLabelMode PageLabel
);
