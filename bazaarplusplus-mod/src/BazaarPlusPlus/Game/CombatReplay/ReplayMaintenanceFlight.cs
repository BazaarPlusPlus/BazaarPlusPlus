#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayMaintenanceFlight : IDisposable
{
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(1);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _flight;
    private bool _disposed;

    internal Task Start(Func<CancellationToken, Task> run)
    {
        if (run == null)
            throw new ArgumentNullException(nameof(run));

        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ReplayMaintenanceFlight));
            if (_flight is { IsCompleted: false })
                return _flight;

            try
            {
                _flight =
                    run(_shutdown.Token)
                    ?? Task.FromException(
                        new InvalidOperationException("Maintenance returned no flight task.")
                    );
            }
            catch (Exception ex)
            {
                _flight = Task.FromException(ex);
            }
            return _flight;
        }
    }

    internal Task Start(Action<CancellationToken> run)
    {
        if (run == null)
            throw new ArgumentNullException(nameof(run));

        return Start(token => Task.Run(() => run(token), token));
    }

    public void Dispose()
    {
        Task? flight;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _shutdown.Cancel();
            flight = _flight;
        }

        var finished = flight == null;
        try
        {
            if (flight != null)
                finished = flight.Wait(ShutdownWait);
        }
        catch (AggregateException)
        {
            // The workflow owner logs its terminal; shutdown itself stays bounded.
            finished = true;
        }
        finally
        {
            // Leave the tiny source alive if a late worker still needs its cancellation token.
            if (finished)
                _shutdown.Dispose();
        }
    }
}
