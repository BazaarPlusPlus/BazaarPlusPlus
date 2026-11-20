#nullable enable

namespace BazaarPlusPlus.Core.Ui;

// Use on the main thread. The build owns cleanup of its partial tree because the slot never receives it.
// Check Unity object liveness through its concrete type because generic reference checks bypass Unity's overloaded equality.
public sealed class TouchViewSlot<TView>
    where TView : class
{
    private readonly Action<TView> _drop;

    public TouchViewSlot(Action<TView> drop)
    {
        _drop = drop ?? throw new ArgumentNullException(nameof(drop));
    }

    public TView? View { get; private set; }

    public string ScreenKey { get; private set; } = string.Empty;

    public bool IsCurrent(string key) =>
        View != null && string.Equals(key, ScreenKey, StringComparison.Ordinal);

    public TView? Ensure(string key, Func<TView> build, Action<Exception> failed) =>
        IsCurrent(key) ? View : Rebuild(key, build, failed);

    public TView? Rebuild(string key, Func<TView> build, Action<Exception> failed)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (build == null)
            throw new ArgumentNullException(nameof(build));
        if (failed == null)
            throw new ArgumentNullException(nameof(failed));
        Drop();
        TView view;
        try
        {
            view = build() ?? throw new InvalidOperationException("The build returned no view.");
        }
        catch (Exception exception)
        {
            failed(exception);
            return null;
        }
        View = view;
        ScreenKey = key;
        return view;
    }

    public void Drop()
    {
        var old = View;
        if (old == null)
            return;
        View = null;
        ScreenKey = string.Empty;
        _drop(old);
    }
}
