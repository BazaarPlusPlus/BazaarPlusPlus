#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The one way a touch control reaches feature code: an exception is logged instead of escaping
/// into uGUI's event loop. Unity's synchronization context already resumes awaits on the main
/// thread here, so unlike the Android host no pump scope is needed.
/// </summary>
internal static class TouchUiActions
{
    public static void OnTap(UnityEngine.UI.Button button, string control, Action handler)
    {
        if (button == null)
            throw new ArgumentNullException(nameof(button));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        button.onClick.AddListener(() => Run(control, handler));
    }

    /// <summary><paramref name="handler"/> gets the field's new text after each edit.</summary>
    public static void OnChanged(TMP_InputField field, string control, Action<string> handler)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        field.onValueChanged.AddListener(value =>
            Run(control, () => handler(value ?? string.Empty))
        );
    }

    public static void Run(string control, Action handler)
    {
        try
        {
            handler();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Touch,
                    "touch.ui.action_failed",
                    storm: ["control"]
                ),
                exception,
                ("control", control)
            );
        }
    }
}

/// <summary>
/// The main thread, captured once from it: a pool-thread completion posts back through Unity's
/// own synchronization context, as the Android host's main-thread pump does.
/// </summary>
internal static class TouchMainThread
{
    private static System.Threading.SynchronizationContext? _context;
    private static int _threadId = -1;

    /// <summary>Main thread, once at startup.</summary>
    public static void Capture()
    {
        _context = System.Threading.SynchronizationContext.Current;
        _threadId = Environment.CurrentManagedThreadId;
    }

    public static bool IsCurrent => Environment.CurrentManagedThreadId == _threadId;

    public static void Post(Action action)
    {
        if (_context == null || IsCurrent)
        {
            action();
            return;
        }
        _context.Post(_ => action(), null);
    }
}

/// <summary>The touch surfaces follow the mobile build: animations are always on.</summary>
internal static class TouchMotion
{
    public static bool AnimationsEnabled() => true;
}

/// <summary>
/// The font every touch surface draws with: the game's own sans face through
/// <see cref="NativeGameTypography"/>, with its CJK fallbacks added when a label needs them.
/// </summary>
internal static class TouchFonts
{
    /// <summary>The game's body font, or null while the game has not loaded it yet.</summary>
    public static TMP_FontAsset? Body()
    {
        var outcome = NativeGameTypography.PrepareOwnedText(out var preparation);
        return outcome == NativeGameTypography.Outcome.Ready ? preparation?.Font : null;
    }

    /// <summary>Adds the game's CJK fallbacks when <paramref name="label"/>'s text needs them.</summary>
    public static void Cover(TMP_Text label)
    {
        if (label == null)
            return;
        try
        {
            NativeGameTypography.EnsureNativeTextCoverage(label, label.text);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.fonts.cover_failed"),
                exception
            );
        }
    }
}
