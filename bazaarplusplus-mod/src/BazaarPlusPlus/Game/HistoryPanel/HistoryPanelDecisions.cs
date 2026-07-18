#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal readonly struct HistoryPanelDatabaseChip
{
    public HistoryPanelDatabaseChip(string text, StatusSeverity severity)
    {
        Text = text;
        Severity = severity;
    }

    public string Text { get; }

    public StatusSeverity Severity { get; }
}

internal static class HistoryPanelDecisions
{
    public static bool CanDeleteRun(
        HistorySectionMode sectionMode,
        HistoryRunRecord? selectedRun,
        bool isInGameRun,
        string? currentServerRunId,
        bool isRepositoryAvailable,
        out string reason
    )
    {
        if (sectionMode == HistorySectionMode.Ghost)
        {
            reason = HistoryPanelText.GhostDeleteUnavailable();
            return false;
        }

        if (selectedRun == null)
        {
            reason = HistoryPanelText.SelectRunToDelete();
            return false;
        }

        if (string.Equals(selectedRun.RawStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            reason = HistoryPanelText.ActiveRunDeleteUnavailable();
            return false;
        }

        if (
            isInGameRun
            && string.Equals(currentServerRunId, selectedRun.RunId, StringComparison.Ordinal)
        )
        {
            reason = HistoryPanelText.CurrentGameplayRunDeleteUnavailable();
            return false;
        }

        if (!isRepositoryAvailable)
        {
            reason = HistoryPanelText.RunLogRepositoryUnavailable();
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static HistoryPanelDatabaseChip ResolveDatabaseChip(
        bool isRepositoryAvailable,
        bool databaseExists
    )
    {
        if (!isRepositoryAvailable)
            return new HistoryPanelDatabaseChip(
                HistoryPanelText.DatabaseChip(HistoryPanelText.DatabaseUnavailable()),
                StatusSeverity.Failure
            );

        return databaseExists
            ? new HistoryPanelDatabaseChip(
                HistoryPanelText.DatabaseChip(HistoryPanelText.DatabaseConnected()),
                StatusSeverity.Success
            )
            : new HistoryPanelDatabaseChip(
                HistoryPanelText.DatabaseChip(HistoryPanelText.DatabaseMissing()),
                StatusSeverity.Neutral
            );
    }

    // Account-link card gate: data sharing must be on AND the build must not be PTR.
    // Unknown is treated like Online by policy (see IGameBuildInfo) so a channel
    // detection failure can never hide the card on a production build.
    internal static bool IsAccountLinkCardAvailable(
        bool dataSharingEnabled,
        GameBuildChannel channel
    ) => dataSharingEnabled && channel != GameBuildChannel.Ptr;
}
