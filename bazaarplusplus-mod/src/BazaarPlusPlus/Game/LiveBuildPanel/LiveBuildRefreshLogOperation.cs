#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

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
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.refresh.succeeded"
            ),
            ("request_id", _requestId, BppLogCorrelationPolicy.Short),
            ("result", result)
        );
        return true;
    }

    internal bool TryFail(LiveBuildRefreshFailureReasonCode reasonCode, Exception? exception = null)
    {
        if (!TryComplete())
            return false;

        var fields = new BppLogField[]
        {
            ("request_id", _requestId, BppLogCorrelationPolicy.Short),
            ("reason_code", reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.refresh.failed"
                ),
                fields
            );
        else
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.refresh.failed"
                ),
                exception,
                fields
            );
        return true;
    }

    private bool TryComplete() => Interlocked.CompareExchange(ref _completed, 1, 0) == 0;
}
