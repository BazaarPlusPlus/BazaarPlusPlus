#nullable enable

namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// The view a touch panel has laid out for one screen key (the Android host's
/// <c>TouchScreen.Key</c>), and the one rule for replacing it. The old view is forgotten,
/// then dropped (the panel's drop returns its cards and destroys its tree), before the new one
/// builds; the key is recorded only once the build returned. A build that throws leaves no view
/// and no key, so the panel reads closed, the next <see cref="Ensure"/> builds again without
/// touching the failed attempt, and the panel's own failure hook recovers its state. A build that
/// throws destroys its own partial tree before the exception leaves: the slot never sees it.
/// Unity-free; main thread only. <see cref="View"/> is compared by reference, never through
/// Unity's overloaded equality, so a panel whose view is a Unity object reads its liveness
/// through a typed local.
/// </summary>
public sealed class TouchViewSlot<TView>
    where TView : class
{
    private readonly Action<TView> _drop;

    public TouchViewSlot(Action<TView> drop)
    {
        _drop = drop ?? throw new ArgumentNullException(nameof(drop));
    }

    public TView? View { get; private set; }

    /// <summary>The key <see cref="View"/> was laid out for; empty while there is no view.</summary>
    public string ScreenKey { get; private set; } = string.Empty;

    public bool IsCurrent(string key) =>
        View != null && string.Equals(key, ScreenKey, StringComparison.Ordinal);

    /// <summary>The current view for <paramref name="key"/>, else <see cref="Rebuild"/>.</summary>
    public TView? Ensure(string key, Func<TView> build, Action<Exception> failed) =>
        IsCurrent(key) ? View : Rebuild(key, build, failed);

    /// <summary>
    /// Drops the view and builds one for <paramref name="key"/>. Null when the build threw:
    /// <paramref name="failed"/> has run with the exception, and a hook that throws propagates.
    /// </summary>
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

    /// <summary>Forgets the view, then drops it; nothing with no view.</summary>
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
