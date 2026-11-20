using BazaarPlusPlus.Android.Patches;

namespace BazaarPlusPlus.Android.Host;

// A native tap handler must capture the main-thread context so its continuations can safely call
// IL2CPP.
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
