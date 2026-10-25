using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// Unity's app pause message, which LemonLoader does not forward to a melon: its callbacks stop at
/// start and quit. This injected <c>MonoBehaviour</c> lives on its own DontDestroyOnLoad object in
/// every patch mode, since the record chain it pauses runs in every mode. Unity calls
/// <c>OnApplicationPause(true)</c> when the activity leaves the foreground and <c>false</c> when it
/// returns; the host acts on that only. Focus is logged but never acted on: the notification shade
/// and system dialogs take focus while the game keeps running in front.
/// </summary>
internal sealed class AndroidAppLifecycle : MonoBehaviour
{
    private static GameObject? _root;

    public AndroidAppLifecycle(IntPtr pointer)
        : base(pointer) { }

    /// <summary>Called on the main thread with true on pause and false on resume.</summary>
    internal static Action<bool>? PauseChanged { get; set; }

    /// <summary>Main thread: registers the type and mounts its object once.</summary>
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
