#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Core.RunContext;

internal sealed class RunContextStore : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public RunExitKind LastRunExitKind { get; set; } = RunExitKind.Completed;

    public EVictoryCondition LastVictoryCondition { get; set; }

    public string LastMessageId { get; set; } = string.Empty;

    public void Reset()
    {
        IsInGameRun = false;
        CurrentServerRunId = null;
        LastRunExitKind = RunExitKind.Completed;
        LastVictoryCondition = default;
        LastMessageId = string.Empty;
    }
}
