using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.GameAuth;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Host;

// IL2CPP calls from an unattached pool thread fault the process before managed exception handling
// can run.
internal static class AndroidHistoryPorts
{
    public static float UnscaledTime() =>
        OnMainThread("unscaled_time") ? UnityEngine.Time.unscaledTime : 0f;

    public static string? ProfileAccountId() =>
        OnMainThread("profile_account") ? AndroidClientCache.TryGetProfileAccountId() : null;

    public static BppGameAuthSnapshot? ReadGameAuthSnapshot() =>
        OnMainThread("game_auth") ? AndroidGameAuthBridge.TryRead() : null;

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
