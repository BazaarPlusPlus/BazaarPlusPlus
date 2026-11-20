using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BazaarPlusPlus.Android.Host;

// LemonLoader does not forward pause events to melons. Focus loss alone also occurs with the
// notification shade open.
internal sealed class AndroidAppLifecycle : MonoBehaviour
{
    private static GameObject? _root;

    public AndroidAppLifecycle(IntPtr pointer)
        : base(pointer) { }

    internal static Action<bool>? PauseChanged { get; set; }

    public static void Mount()
    {
        if (_root != null)
            return;
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<AndroidAppLifecycle>();
            var root = new GameObject("BPP.AppLifecycle");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<AndroidAppLifecycle>();
            _root = root;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.app_lifecycle.mounted")
            );
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.app_lifecycle.mount_failed"),
                exception
            );
        }
    }

    private void OnApplicationPause(bool paused)
    {
        BppLog.InfoEvent(
            paused
                ? new BppLogEvent(BppLogFeatureScope.Android, "android.app_lifecycle.paused")
                : new BppLogEvent(BppLogFeatureScope.Android, "android.app_lifecycle.resumed")
        );
        PauseChanged?.Invoke(paused);
    }

    private void OnApplicationFocus(bool focused) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.app_lifecycle.focus"),
            ("focused", focused)
        );
}
