#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.RunLifecycle;

/// <summary>The desktop's run lifecycle signals: the Mono game's <c>TheBazaar.Events</c>.</summary>
internal sealed class GameRunLifecycleSource : IRunLifecycleSource
{
    public IDisposable Subscribe(Action runStarted, Action runEnded, Action runInterrupted)
    {
        TheBazaar.Events.RunStarted.AddListener(runStarted, null);
        TheBazaar.Events.RunEnded.AddListener(runEnded, null);
        TheBazaar.Events.RunInterrupted.AddListener(runInterrupted, null);
        return new Subscription(runStarted, runEnded, runInterrupted);
    }

    public BppLogField[] DescribeState() =>
        [
            ("app_state", AppState.CurrentState?.GetType().Name ?? "null"),
            ("run_state", Data.CurrentState?.StateName.ToString() ?? "null"),
            ("has_active_run", Data.HasActiveRun),
        ];

    private sealed class Subscription(Action runStarted, Action runEnded, Action runInterrupted)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            TheBazaar.Events.RunStarted.RemoveListener(runStarted);
            TheBazaar.Events.RunEnded.RemoveListener(runEnded);
            TheBazaar.Events.RunInterrupted.RemoveListener(runInterrupted);
        }
    }
}
