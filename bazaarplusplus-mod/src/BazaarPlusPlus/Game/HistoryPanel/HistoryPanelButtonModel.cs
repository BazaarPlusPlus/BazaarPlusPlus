#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal readonly struct HistoryPanelButtonModel
{
    public HistoryPanelButtonModel(
        string replayButtonText,
        bool replayButtonEnabled,
        string recordAndReplayButtonText,
        bool recordAndReplayButtonEnabled,
        string deleteButtonText,
        bool deleteButtonEnabled
    )
    {
        ReplayButtonText = replayButtonText;
        ReplayButtonEnabled = replayButtonEnabled;
        RecordAndReplayButtonText = recordAndReplayButtonText;
        RecordAndReplayButtonEnabled = recordAndReplayButtonEnabled;
        DeleteButtonText = deleteButtonText;
        DeleteButtonEnabled = deleteButtonEnabled;
    }

    public string ReplayButtonText { get; }

    public bool ReplayButtonEnabled { get; }

    public string RecordAndReplayButtonText { get; }

    public bool RecordAndReplayButtonEnabled { get; }

    public string DeleteButtonText { get; }

    public bool DeleteButtonEnabled { get; }

    public static HistoryPanelButtonModel Build(
        bool replayActionInProgress,
        bool canReplaySelectedBattle,
        string replayUnavailableReason,
        string replayActionLabel,
        bool isInGameRun,
        bool canRecordSelectedBattle,
        bool isDeleteConfirmationActive,
        bool canDeleteSelectedRun
    )
    {
        return new HistoryPanelButtonModel(
            ResolveReplayButtonText(
                replayActionInProgress,
                canReplaySelectedBattle,
                replayUnavailableReason,
                replayActionLabel,
                isInGameRun
            ),
            canReplaySelectedBattle && !replayActionInProgress,
            HistoryPanelText.RecordAndReplay(),
            canRecordSelectedBattle && !replayActionInProgress,
            isDeleteConfirmationActive
                ? HistoryPanelText.DeleteConfirm()
                : HistoryPanelText.Delete(),
            canDeleteSelectedRun
        );
    }

    private static string ResolveReplayButtonText(
        bool replayActionInProgress,
        bool canReplaySelectedBattle,
        string replayUnavailableReason,
        string replayActionLabel,
        bool isInGameRun
    )
    {
        if (replayActionInProgress)
            return HistoryPanelText.Working();

        if (canReplaySelectedBattle)
            return replayActionLabel;

        if (isInGameRun)
            return HistoryPanelText.ReplayDisabledInRun();

        return string.IsNullOrWhiteSpace(replayUnavailableReason)
            ? replayActionLabel
            : HistoryPanelText.ReplayUnavailable();
    }
}
