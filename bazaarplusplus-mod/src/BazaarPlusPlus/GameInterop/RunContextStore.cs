#nullable enable
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.GameInterop;

internal sealed class RunContextStore : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public RunExitKind LastRunExitKind { get; set; } = RunExitKind.Completed;
}
