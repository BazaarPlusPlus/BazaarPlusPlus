#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

// One running read and one replaceable intent. Publication resumes on the caller's context.
internal sealed class LatestHistoryRead<T>
    where T : class
{
    private sealed record Work(int Version, Func<T?> Read, Action<T?, Exception?> Publish);

    private Work? _next;
    private bool _running;
    private int _version;

    internal void Clear()
    {
        _version++;
        _next = null;
    }

    internal void Submit(Func<T?> read, Action<T?, Exception?> publish)
    {
        _next = new(++_version, read, publish);
        if (!_running)
            _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        _running = true;
        try
        {
            while (_next is { } work)
            {
                _next = null;
                T? result = null;
                Exception? error = null;
                try
                {
                    result = await Task.Run(work.Read);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                if (work.Version == _version)
                    work.Publish(result, error);
            }
        }
        finally
        {
            _running = false;
        }
    }
}
