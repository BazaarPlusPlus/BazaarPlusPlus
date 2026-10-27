#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>UI-thread refresh requests consumed once by the host's visible tick.</summary>
public sealed class HistoryPanelUiRefresh
{
    private bool _dirty;
    private bool _flushing;

    // Selection-dependent inputs must not act on a model awaiting replacement.
    public bool CanAct => !_dirty && !_flushing;

    public void Request() => _dirty = true;

    public void Flush(Action render)
    {
        if (!_dirty || _flushing)
            return;
        _dirty = false;
        _flushing = true;
        try
        {
            render();
        }
        catch
        {
            _dirty = true;
            throw;
        }
        finally
        {
            _flushing = false;
        }
    }

    public Action Guard(Action action) =>
        () =>
        {
            if (CanAct)
                action();
        };

    public Action<T> Guard<T>(Action<T> action) =>
        value =>
        {
            if (CanAct)
                action(value);
        };
}
