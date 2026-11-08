#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// One render of History for a host's view: every list, label and button state the panel shows,
/// built by <see cref="HistoryPanelViewModelBuilder"/> so both hosts render the same panel.
/// </summary>
public sealed class HistoryPanelViewModel
{
    // Stands in for the detail when the section has nothing to show there (HistoryArchiveStatus).
    public string ArchiveEmptyMessage { get; set; } = string.Empty;
    public string? AccountId { get; set; }

    // Gates the paging and action controls; the page label text comes from PageLabel.
    public bool PageLoading { get; set; }
    public bool HasNewer { get; set; }
    public bool HasOlder { get; set; }
    public bool BattleHasNewer { get; set; }
    public bool BattleHasOlder { get; set; }
    public string PageLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        new List<BPPSupporterSample>();

    public string DatabaseChipText { get; set; } = string.Empty;

    public string ServerHealthButtonText { get; set; } = string.Empty;

    public bool ServerHealthButtonEnabled { get; set; }

    public string AccountPersistenceText { get; set; } = string.Empty;

    public string AccountTitleText { get; set; } = string.Empty;

    public string AccountWhyText { get; set; } = string.Empty;

    public string AccountHintText { get; set; } = string.Empty;

    public string AccountRowStatusText { get; set; } = string.Empty;

    public string AccountRowActionText { get; set; } = string.Empty;

    public bool AccountRowActionVisible { get; set; }

    public string AccountLinkCollapseText { get; set; } = string.Empty;

    public string AccountLinkButtonText { get; set; } = string.Empty;

    public string AccountAlreadyLinkedButtonText { get; set; } = string.Empty;

    public bool AccountAlreadyLinkedButtonVisible { get; set; }

    public bool AccountLinkButtonEnabled { get; set; }

    public bool AccountLinkInputEnabled { get; set; }

    public string? AccountLinkBannerText { get; set; }

    public StatusSeverity AccountLinkBannerSeverity { get; set; }

    public bool AccountLinkFormVisible { get; set; }

    public HistorySectionMode SectionMode { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; }

    public string? SelectedRunHero { get; set; }

    public bool GhostDayMin10 { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    public StatusSeverity DatabaseChipSeverity { get; set; }

    public List<HistoryRunRecord> Runs { get; set; } = new();

    public List<HistoryBattleRecord> VisibleBattles { get; set; } = new();

    // One per row of Runs (runs) or of Ghost and Favorites (VisibleBattles), in the same order.
    public List<HistoryArchiveRow> ArchiveRows { get; set; } = new();

    // The selected run's battles, in VisibleBattles order; empty outside Runs.
    public List<HistoryTimelineItem> Timeline { get; set; } = new();

    public HistoryBoardTitles BoardTitles { get; set; }

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public string ReplayButtonText { get; set; } = string.Empty;

    public bool ReplayButtonEnabled { get; set; }

    // An unavailable replay stays pressable to explain its reason; a busy one does not.
    public bool ReplayButtonBusy { get; set; }

    public string RecordAndReplayButtonText { get; set; } = string.Empty;

    public bool RecordAndReplayButtonEnabled { get; set; }

    // False on a host that records no video: the record action is not shown.
    public bool RecordAndReplayButtonVisible { get; set; }

    public bool DeleteButtonVisible { get; set; }

    public string DeleteButtonText { get; set; } = string.Empty;

    public bool DeleteButtonEnabled { get; set; }

    public bool DeleteButtonBusy { get; set; }

    public bool ShowsBothBoards { get; set; }

    public bool FavoritesTabVisible { get; set; }

    public bool FavoriteButtonVisible { get; set; }

    public string FavoriteButtonText { get; set; } = string.Empty;

    public bool FavoriteButtonEnabled { get; set; }
}
