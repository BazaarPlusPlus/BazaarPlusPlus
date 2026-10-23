namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Whether a combat is running, as the desktop's overlay panel host reads it
/// (<c>Data.IsInCombat</c>); an unreadable state reads as no combat. Main thread only.
/// </summary>
internal static class AndroidCombatProbe
{
    public static bool IsInCombat()
    {
        try
        {
            return Il2CppTheBazaar.Data.IsInCombat;
        }
        catch
        {
            return false;
        }
    }
}
