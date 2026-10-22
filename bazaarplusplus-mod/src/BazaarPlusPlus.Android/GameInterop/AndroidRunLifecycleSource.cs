using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The run lifecycle signals of the IL2CPP game: the proxies of <c>TheBazaar.Events.RunStarted</c>,
/// <c>RunEnded</c>, and <c>RunInterrupted</c>. Their listeners are <c>Il2CppSystem.Action</c>, so
/// each handler is converted once and the same IL2CPP delegate is removed on dispose (the game keys
/// its wrappers by delegate). Each signal counts its hits for the hooks summary.
/// </summary>
internal sealed class AndroidRunLifecycleSource : IRunLifecycleSource
{
    private long _started;
    private long _ended;
    private long _interrupted;

    public IDisposable Subscribe(Action runStarted, Action runEnded, Action runInterrupted)
    {
        var started = Convert(() =>
        {
            Interlocked.Increment(ref _started);
            runStarted();
        });
        var ended = Convert(() =>
        {
            Interlocked.Increment(ref _ended);
            runEnded();
        });
        var interrupted = Convert(() =>
        {
            Interlocked.Increment(ref _interrupted);
            runInterrupted();
        });
        Events.RunStarted.AddListener(started, null);
        Events.RunEnded.AddListener(ended, null);
        Events.RunInterrupted.AddListener(interrupted, null);
        return new Subscription(started, ended, interrupted);
    }

    public BppLogField[] DescribeState() =>
        [
            ("app_state", AppState.CurrentState?.GetIl2CppType().Name ?? "null"),
            ("run_state", Data.CurrentState?.StateName.ToString() ?? "null"),
            ("has_active_run", Data.HasActiveRun),
        ];

    public BppLogField[] Summary() =>
        [
            ("run_started_hits", Interlocked.Read(ref _started)),
            ("run_ended_hits", Interlocked.Read(ref _ended)),
            ("run_interrupted_hits", Interlocked.Read(ref _interrupted)),
        ];

    private static Il2CppSystem.Action Convert(Action handler) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(handler)
        ?? throw new InvalidOperationException("Il2CppInterop returned no delegate.");

    private sealed class Subscription(
        Il2CppSystem.Action started,
        Il2CppSystem.Action ended,
        Il2CppSystem.Action interrupted
    ) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Events.RunStarted.RemoveListener(started);
            Events.RunEnded.RemoveListener(ended);
            Events.RunInterrupted.RemoveListener(interrupted);
        }
    }
}
