#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Core.GameState;

/// <summary>
/// The game's run lifecycle signals as one host reads them: the desktop subscribes to the Mono
/// <c>TheBazaar.Events</c>, the Android host to their IL2CPP proxies.
/// </summary>
public interface IRunLifecycleSource
{
    /// <summary>
    /// Calls the handlers on the game thread when a run starts, ends (victory or defeat), or is
    /// interrupted (back to the main menu, rate limit). Disposing the result unsubscribes.
    /// </summary>
    IDisposable Subscribe(Action runStarted, Action runEnded, Action runInterrupted);

    /// <summary>The game's own state fields that a run-state change is logged with.</summary>
    BppLogField[] DescribeState();
}
