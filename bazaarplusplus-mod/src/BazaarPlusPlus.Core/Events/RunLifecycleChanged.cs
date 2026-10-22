#nullable enable
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.Core.Events;

public sealed class RunLifecycleChanged
{
    public bool IsInGameRun { get; set; }

    public RunExitKind LastRunExitKind { get; set; }

    public string Reason { get; set; } = string.Empty;
}
