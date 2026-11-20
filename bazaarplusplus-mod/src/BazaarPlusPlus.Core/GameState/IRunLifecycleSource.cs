#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Core.GameState;

public interface IRunLifecycleSource
{
    // Host implementations invoke handlers on the game thread.
    IDisposable Subscribe(Action runStarted, Action runEnded, Action runInterrupted);

    BppLogField[] DescribeState();
}
