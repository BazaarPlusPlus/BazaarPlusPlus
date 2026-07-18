#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildRefreshLogOperation
{
    private readonly Guid _requestId;
    private int _completed;

    internal LiveBuildRefreshLogOperation(Guid requestId)
    {
        _requestId = requestId;
    }

    internal bool TrySucceed(LiveBuildRefreshResultCode result)
    {
        if (!TryComplete())
            return false;

        BppLog.InfoEvent(
            LiveBuildPanelLogEvents.RefreshSucceeded,
            LiveBuildPanelLogEvents.RefreshSucceededRequestId.Bind(_requestId),
            LiveBuildPanelLogEvents.RefreshSucceededResult.Bind(result)
        );
        return true;
    }

    internal bool TryFail(LiveBuildRefreshFailureReasonCode reasonCode, Exception? exception = null)
    {
        if (!TryComplete())
            return false;

        var fields = new[]
        {
            LiveBuildPanelLogEvents.RefreshFailedRequestId.Bind(_requestId),
            LiveBuildPanelLogEvents.RefreshFailedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(LiveBuildPanelLogEvents.RefreshFailed, fields);
        else
            BppLog.ErrorEvent(LiveBuildPanelLogEvents.RefreshFailed, exception, fields);
        return true;
    }

    private bool TryComplete() => Interlocked.CompareExchange(ref _completed, 1, 0) == 0;
}
