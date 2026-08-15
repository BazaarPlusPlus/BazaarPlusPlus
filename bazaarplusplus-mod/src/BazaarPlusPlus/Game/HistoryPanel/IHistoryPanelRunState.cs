#nullable enable
using BazaarPlusPlus.GameInterop;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// Read-only run presence facts History Panel needs from the live game session.
/// Paths, clients, and replay accessors are assembled separately and do not belong here.
/// </summary>
internal interface IHistoryPanelRunState
{
    bool IsInGameRun { get; }

    string? CurrentServerRunId { get; }
}

/// <summary>
/// Thin production adapter over <see cref="IRunContext"/> — exposes only the two read
/// surfaces History Panel consumes; never setters or unrelated run-exit fields.
/// </summary>
internal sealed class HistoryPanelRunState : IHistoryPanelRunState
{
    private readonly IRunContext _runContext;

    public HistoryPanelRunState(IRunContext runContext)
    {
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
    }

    public bool IsInGameRun => _runContext.IsInGameRun;

    public string? CurrentServerRunId => _runContext.CurrentServerRunId;
}
