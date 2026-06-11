#nullable enable
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.GameInterop;

internal interface IRunContext
{
    bool IsInGameRun { get; set; }

    string? CurrentServerRunId { get; set; }

    RunExitKind LastRunExitKind { get; set; }

    RunVictoryOutcome LastVictoryOutcome { get; set; }

    string LastMessageId { get; set; }
}
