#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

internal sealed class BazaarAgentListenerLogState
{
    private bool _hasStarted;
    private bool _isDegraded;

    internal void OnStartSucceeded(int port, IBazaarAgentLogger logger)
    {
        if (_isDegraded)
        {
            _isDegraded = false;
            _hasStarted = true;
            logger.TryEmit(BazaarAgentLogEvents.ListenerRecovered(port));
            return;
        }

        if (_hasStarted)
            return;
        _hasStarted = true;
        logger.TryEmit(BazaarAgentLogEvents.ListenerStarted(port));
    }

    internal void OnStartFailed(int port, Exception exception, IBazaarAgentLogger logger)
    {
        if (_isDegraded)
            return;
        _isDegraded = true;
        logger.TryEmit(BazaarAgentLogEvents.ListenerDegraded(port, exception));
    }
}
