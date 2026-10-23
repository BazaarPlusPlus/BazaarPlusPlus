using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The one way a History or replay control reaches feature code: the handler runs inside the
/// main-thread pump's scope (<see cref="AndroidMainThread.Enter"/>), so every <c>await</c> the
/// coordinator starts from a tap resumes on the main thread, and an exception is reported
/// instead of escaping into uGUI's event loop. A handler wired any other way would resume its
/// continuations on a pool thread and call into IL2CPP from there.
/// </summary>
internal static class AndroidUiActions
{
    public static void OnTap(Button button, string control, Action handler)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(handler);
        button.onClick.AddListener((UnityAction)(() => Run(control, handler)));
    }

    public static void Run(string control, Action handler)
    {
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            handler();
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError($"ui:{control}", exception);
        }
    }
}
