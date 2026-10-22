#nullable enable
namespace BazaarPlusPlus.Core.RunContext;

public sealed class RunContextStore : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public RunExitKind LastRunExitKind { get; set; } = RunExitKind.Completed;
}
