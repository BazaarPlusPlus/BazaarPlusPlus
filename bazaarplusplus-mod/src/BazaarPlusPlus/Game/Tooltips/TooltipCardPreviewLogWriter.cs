#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipCardPreviewLogWriter
{
    internal static Action<NativeCardPreviewFailure>? Reporter
    {
        get
        {
#if DEBUG
            return Report;
#else
            return null;
#endif
        }
    }

    private static void Report(NativeCardPreviewFailure failure)
    {
        var values = new[]
        {
            TooltipLogEvents.CardPreviewHoverFailedOperation.Bind(failure.Operation),
            TooltipLogEvents.CardPreviewHoverFailedReasonCode.Bind(failure.Reason),
        };
        if (failure.Exception == null)
            BppLog.DebugEvent(TooltipLogEvents.CardPreviewHoverFailed, () => values);
        else
            BppLog.DebugEvent(
                TooltipLogEvents.CardPreviewHoverFailed,
                failure.Exception,
                () => values
            );
    }
}
