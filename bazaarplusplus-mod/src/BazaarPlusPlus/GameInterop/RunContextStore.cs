#nullable enable
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.GameInterop;

internal sealed class RunContextStore : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public RunExitKind LastRunExitKind { get; set; } = RunExitKind.Completed;

    public RunVictoryOutcome LastVictoryOutcome { get; set; }

    public string LastMessageId { get; set; } = string.Empty;

    public void Reset()
    {
        IsInGameRun = false;
        CurrentServerRunId = null;
        LastRunExitKind = RunExitKind.Completed;
        LastVictoryOutcome = default;
        LastMessageId = string.Empty;
    }
}
