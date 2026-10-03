#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum HistoryPanelCancellationDisposition
{
    AbandonStaleRequest,
    FailCurrentRequest,
}

internal static class HistoryPanelCancellationRouter
{
    internal static HistoryPanelCancellationDisposition Resolve(bool isCurrentSession) =>
        isCurrentSession
            ? HistoryPanelCancellationDisposition.FailCurrentRequest
            : HistoryPanelCancellationDisposition.AbandonStaleRequest;
}
