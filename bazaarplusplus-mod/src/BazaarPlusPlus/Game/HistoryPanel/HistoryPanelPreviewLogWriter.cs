#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelPreviewLogWriter
{
    internal static void ReportCardPreview(NativeCardPreviewFailure failure)
    {
        var fields = new BppLogField[]
        {
            ("operation", failure.Operation),
            ("reason_code", failure.Reason),
            ("template_id", failure.TemplateId),
        };
        if (failure.Exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.card_preview.degraded",
                    storm: ["operation", "reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.card_preview.degraded",
                    storm: ["operation", "reason_code"]
                ),
                failure.Exception,
                fields
            );
    }
}
