#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelPreviewLogWriter
{
    internal static void ReportCardPreview(NativeCardPreviewFailure failure)
    {
        var fields = new[]
        {
            HistoryPanelLogEvents.CardPreviewDegradedOperation.Bind(failure.Operation),
            HistoryPanelLogEvents.CardPreviewDegradedReasonCode.Bind(failure.Reason),
            HistoryPanelLogEvents.CardPreviewDegradedTemplateId.Bind(failure.TemplateId),
        };
        if (failure.Exception == null)
            BppLog.WarnEvent(HistoryPanelLogEvents.CardPreviewDegraded, fields);
        else
            BppLog.WarnEvent(HistoryPanelLogEvents.CardPreviewDegraded, failure.Exception, fields);
    }

    internal static void ReportItemBoard(ItemBoardPreviewFailure failure)
    {
        var fields = new[]
        {
            HistoryPanelLogEvents.ItemBoardPreviewDegradedOperation.Bind(failure.Operation),
            HistoryPanelLogEvents.ItemBoardPreviewDegradedReasonCode.Bind(failure.Reason),
            HistoryPanelLogEvents.ItemBoardPreviewDegradedTemplateId.Bind(failure.TemplateId),
        };
        if (failure.Exception == null)
            BppLog.WarnEvent(HistoryPanelLogEvents.ItemBoardPreviewDegraded, fields);
        else
            BppLog.WarnEvent(
                HistoryPanelLogEvents.ItemBoardPreviewDegraded,
                failure.Exception,
                fields
            );
    }
}
