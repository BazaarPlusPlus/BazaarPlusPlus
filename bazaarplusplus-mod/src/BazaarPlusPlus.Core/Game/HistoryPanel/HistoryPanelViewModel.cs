#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.HistoryPanel;

public sealed class HistoryPanelViewModel
{
    // A Bind Code is 8 characters; the room left covers a pasted "XXXX-XXXX" with stray spaces.
    public const int BppAccountBindCodeMaxLength = 16;

    public string ArchiveEmptyMessage { get; set; } = string.Empty;
    public string? AccountId { get; set; }

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

    public string BppAccountTitleText { get; set; } = string.Empty;

    public string BppAccountStatusText { get; set; } = string.Empty;

    public string BppAccountActionText { get; set; } = string.Empty;

    public bool BppAccountActionVisible { get; set; }

    public bool BppAccountFormVisible { get; set; }

    public string BppAccountFormPromptText { get; set; } = string.Empty;

    public string BppAccountNoticeText { get; set; } = string.Empty;

    public string BppAccountInputPlaceholder { get; set; } = string.Empty;

    public string BppAccountSubmitText { get; set; } = string.Empty;

    public bool BppAccountControlsEnabled { get; set; }

    public string? BppAccountBannerText { get; set; }

    public StatusSeverity BppAccountBannerSeverity { get; set; }

    public HistorySectionMode SectionMode { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; }

    public string? SelectedRunHero { get; set; }

    public bool GhostDayMin10 { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    public StatusSeverity DatabaseChipSeverity { get; set; }

    public List<HistoryRunRecord> Runs { get; set; } = new();

    public List<HistoryBattleRecord> VisibleBattles { get; set; } = new();

    public List<HistoryArchiveRow> ArchiveRows { get; set; } = new();

    public List<HistoryTimelineItem> Timeline { get; set; } = new();

    public HistoryBoardTitles BoardTitles { get; set; }

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public string ReplayButtonText { get; set; } = string.Empty;

    public bool ReplayButtonEnabled { get; set; }

    public bool ReplayButtonBusy { get; set; }

    public string RecordAndReplayButtonText { get; set; } = string.Empty;

    public bool RecordAndReplayButtonEnabled { get; set; }

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
