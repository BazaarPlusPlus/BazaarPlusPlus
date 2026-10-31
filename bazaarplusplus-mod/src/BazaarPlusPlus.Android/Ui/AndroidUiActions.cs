using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The one way a host control reaches feature code, and the Mobile Platform Port's
/// <c>RunUiAction</c>: the handler runs inside the main-thread pump's scope
/// (<see cref="AndroidMainThread.Enter"/>), so every <c>await</c> the coordinator starts from a
/// tap resumes on the main thread, and an exception is reported instead of escaping into uGUI's
/// event loop. A handler wired any other way would resume its
/// continuations on a pool thread and call into IL2CPP from there.
/// </summary>
internal static class AndroidUiActions
{
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
