#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentListenerStopReport
{
    public int FailedPhaseCount { get; private set; }
    public BazaarAgentListenerStopPhase FirstFailedPhase { get; private set; }
    public Exception? FirstException { get; private set; }

    internal void Capture(BazaarAgentListenerStopPhase phase, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Add(phase, exception);
        }
    }

    private void Add(BazaarAgentListenerStopPhase phase, Exception exception)
    {
        if (FailedPhaseCount == 0)
        {
            FirstFailedPhase = phase;
            FirstException = exception;
        }
        FailedPhaseCount++;
    }
}
