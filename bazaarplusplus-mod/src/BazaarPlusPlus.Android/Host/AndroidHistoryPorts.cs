using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The host ports History's coordinator reads (<c>HistoryPanelDependencies</c>): the frame
/// clock, the signed-in profile's account, and the BazaarDB link-hint store over
/// <c>PlayerPrefs</c>. Each calls into IL2CPP, which from an unattached pool thread is a native
/// fault rather than a catchable exception, so each checks the thread first, in release builds
/// too: off the main thread it answers 0, null, or nothing, and logs
/// <c>android.main_thread.port_off_thread</c>.
/// </summary>
internal static class AndroidHistoryPorts
{
    public static float UnscaledTime() =>
        OnMainThread("unscaled_time") ? UnityEngine.Time.unscaledTime : 0f;

    public static string? ProfileAccountId() =>
        OnMainThread("profile_account") ? AndroidClientCache.TryGetProfileAccountId() : null;

    public static BazaarDbAccountLinkStore AccountLinkStore() =>
        new(
            key => OnMainThread("account_link_hint") && UnityEngine.PlayerPrefs.HasKey(key),
            key =>
            {
                if (!OnMainThread("account_link_hint"))
                    return;
                UnityEngine.PlayerPrefs.SetString(key, "1");
                UnityEngine.PlayerPrefs.Save();
            }
        );

    private static bool OnMainThread(string port)
    {
        if (AndroidMainThread.Host.IsCurrentThread)
            return true;
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.Android,
                "android.main_thread.port_off_thread",
                storm: ["port"]
            ),
            ("port", port),
            ("thread_id", Environment.CurrentManagedThreadId)
        );
        return false;
    }
}
