#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static class LiveBuildPreviewLogWriter
{
    internal static void ReportCardPreview(NativeCardPreviewFailure failure)
    {
        var fields = new[]
        {
            LiveBuildPanelLogEvents.CardPreviewDegradedOperation.Bind(failure.Operation),
            LiveBuildPanelLogEvents.CardPreviewDegradedReasonCode.Bind(failure.Reason),
            LiveBuildPanelLogEvents.CardPreviewDegradedTemplateId.Bind(failure.TemplateId),
        };
        if (failure.Exception == null)
            BppLog.WarnEvent(LiveBuildPanelLogEvents.CardPreviewDegraded, fields);
        else
            BppLog.WarnEvent(
                LiveBuildPanelLogEvents.CardPreviewDegraded,
                failure.Exception,
                fields
            );
    }
}
