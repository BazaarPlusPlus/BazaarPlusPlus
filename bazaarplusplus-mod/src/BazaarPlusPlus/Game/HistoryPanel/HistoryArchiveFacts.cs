#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum HistoryDetailPhase
{
    // DetailBattleId names another battle (or none): nothing is known about this one's snapshots.
    NotSelectedBattle,

    // Global, as the detail read is: any pending read shows loading.
    Loading,
    Missing,
    Loaded,
}

internal enum HistoryPageLabelMode
{
    Loading,
    Totals,
    Hidden,
}

// Status is null until the board reports; the decision shows no message for it.
internal readonly record struct HistoryBoardFacts(
    NativeMonsterBoardStatus? Status,
    int PartialCount
);

// The selected Ghost battle's replay; the progress and failure are this battle's own.
internal readonly record struct HistorySelectedGhostReplay(
    ReplayAvailability Availability,
    bool ActionInProgress,
    string? FailureMessage
);

// What HistoryPanelDecisions.ArchiveStatus reads. PageLoading/PageLoadFailed stay two bools
// because LoadArchive, their only writer, sets them together; Loading wins when both are set.
internal sealed record HistoryArchiveFacts(
    HistorySectionMode Section,
    bool AccountKnown,
    bool PageLoading,
    bool PageLoadFailed,
    GhostSyncPhase GhostSync,
    bool FiltersActive,
    int RowCount,
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
            state.GhostBattleFilter != GhostBattleFilter.All || state.GhostDayMin10,
            ghost ? state.GhostBattles.Count : state.Runs.Count,
            selected is { Source: HistoryBattleSource.Ghost }
                ? new HistorySelectedGhostReplay(
                    selected.Replay,
                    ownsReplayAction && state.ReplayActionInProgress,
                    ownsReplayAction ? state.ReplayFailureMessage : null
                )
                : null,
            state.DetailLoading ? HistoryDetailPhase.Loading
                : selected == null || state.DetailBattleId != selected.BattleId
                    ? HistoryDetailPhase.NotSelectedBattle
                : state.DetailSnapshots == null ? HistoryDetailPhase.Missing
                : HistoryDetailPhase.Loaded,
            state.DetailFailed,
            playerBoard,
            opponentBoard
        );
    }
}

internal readonly record struct HistoryArchiveStatus(
    string ListMessage,
    string PlayerBoardMessage,
    string OpponentBoardMessage,
    HistoryPageLabelMode PageLabel
);
