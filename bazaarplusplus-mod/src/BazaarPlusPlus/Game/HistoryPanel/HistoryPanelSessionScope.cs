#nullable enable
using System;
using System.Threading;

namespace BazaarPlusPlus.Game.HistoryPanel;

// Owns the cancellation token + version stamp shared by the panel's long-running async actions
// (ghost sync, replay download, final-build refresh). Bumping the version on every Begin/End
// means async continuations can detect whether they are still serving the active session.
internal sealed class HistoryPanelSessionScope : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _version;

    public int Version => _version;

    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

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
        catch
        {
            // Cancellation is best-effort during teardown.
        }

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
