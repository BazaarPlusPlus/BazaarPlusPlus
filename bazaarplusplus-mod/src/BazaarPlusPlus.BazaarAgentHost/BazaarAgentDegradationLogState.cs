#nullable enable
using BazaarPlusPlus.BazaarAgent;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentDegradationLogState
{
    private bool _degraded;

    internal void ReportDegraded(IBazaarAgentLogger logger, Func<BazaarAgentLogEvent> eventFactory)
    {
        if (_degraded)
            return;
        _degraded = true;
        logger.TryEmit(eventFactory());
    }

    internal void ReportRecovered(IBazaarAgentLogger logger, Func<BazaarAgentLogEvent> eventFactory)
    {
        if (!_degraded)
            return;
        _degraded = false;
        logger.TryEmit(eventFactory());
    }
}
