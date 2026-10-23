#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

public enum HistoryPanelCancellationDisposition
{
    AbandonStaleRequest,
    FailCurrentRequest,
}

public static class HistoryPanelCancellationRouter
{
    internal static HistoryPanelCancellationDisposition Resolve(bool isCurrentSession) =>
        isCurrentSession
            ? HistoryPanelCancellationDisposition.FailCurrentRequest
            : HistoryPanelCancellationDisposition.AbandonStaleRequest;
}
