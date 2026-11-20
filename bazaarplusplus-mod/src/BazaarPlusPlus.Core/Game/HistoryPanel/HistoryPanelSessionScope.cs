#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

// Bump the version on each session boundary so stale async continuations can drop their results.
public sealed class HistoryPanelSessionScope : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _version;

    public int Version => _version;

    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    public bool IsActive => _cts != null;

    public void Begin()
    {
        End();
        _version++;
        _cts = new CancellationTokenSource();
    }

    public void End()
    {
        _version++;
        if (_cts == null)
            return;

        try
        {
            _cts.Cancel();
        }
        catch { }

        _cts.Dispose();
        _cts = null;
    }

    public bool IsCurrent(int versionSnapshot)
    {
        return versionSnapshot == _version;
    }

    public void Dispose()
    {
        End();
    }
}
