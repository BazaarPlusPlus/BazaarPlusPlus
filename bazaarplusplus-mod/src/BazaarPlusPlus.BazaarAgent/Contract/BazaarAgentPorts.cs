#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentRuntimeDefaults
{
    public const int HttpListenerPort = 47900;
    public const int ActionTimeoutMilliseconds = 3000;
    public static readonly TimeSpan ActionMinDelay = TimeSpan.FromSeconds(1);
}

public interface IBazaarAgentOptions
{
    string DecisionLogRoot { get; }
}

public interface IBazaarAgentContextReader
{
    BazaarAgentContext Build(double actionCooldownRemainingSeconds);
}

/// <summary>Optional capability for context readers that retain a post-combat summary.</summary>
public interface IBazaarAgentBattleSummaryAcknowledger
{
    void AcknowledgeLastBattle();
}

public interface IBazaarAgentActionDispatcher
{
    BazaarAgentDispatchResult Execute(
        BazaarAgentAction action,
        BazaarAgentContextSnapshot snapshot
    );
}

public enum BazaarAgentDispatchDiagnostic
{
    None,
    DispatcherException,
}

public enum BazaarAgentDispatchFailureKind
{
    Internal,
    Invalid,
    Unavailable,
}

public readonly record struct BazaarAgentDispatchResult(
    bool Executed,
    string? Error,
    BazaarAgentDispatchDiagnostic Diagnostic = BazaarAgentDispatchDiagnostic.None,
    Exception? DiagnosticException = null,
    BazaarAgentDispatchFailureKind FailureKind = BazaarAgentDispatchFailureKind.Internal
);

public interface IBazaarAgentLogger
{
    void Emit(BazaarAgentLogEvent logEvent);
}

public interface IBazaarAgentClock
{
    double NowSeconds { get; }

    string UtcNowIsoString();
}
