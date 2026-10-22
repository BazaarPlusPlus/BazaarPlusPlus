#nullable enable
namespace BazaarPlusPlus.Core.RunContext;

public interface IRunContext
{
    bool IsInGameRun { get; set; }

    string? CurrentServerRunId { get; set; }

    RunExitKind LastRunExitKind { get; set; }
}
