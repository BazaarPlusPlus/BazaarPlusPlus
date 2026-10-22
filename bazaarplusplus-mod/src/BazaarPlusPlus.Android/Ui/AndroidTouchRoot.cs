using System.Collections;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The host's one managed <c>MonoBehaviour</c>, registered with the IL2CPP runtime through
/// <see cref="ClassInjector"/> and mounted on the touch UI's root. Unity calls its
/// <c>Update</c> every frame while the root lives, which drives the touch UI; destroying the root
/// ends that. The first frame also proves the coroutine boundary once: a managed iterator runs
/// through <see cref="MelonCoroutines"/>, and the log says how far it got.
/// </summary>
internal sealed class AndroidTouchRoot : MonoBehaviour
{
    private static bool _registered;
    private static bool _coroutineProbed;

    public AndroidTouchRoot(IntPtr pointer)
        : base(pointer) { }

    /// <summary>Called by <see cref="AndroidTouchUi"/> each frame through Unity.</summary>
    internal static Action? Frame { get; set; }

    /// <summary>Registers the type once; <c>AddComponent</c> fails for an unregistered type.</summary>
    public static bool Register()
    {
        if (_registered)
            return true;
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<AndroidTouchRoot>();
            _registered = true;
            BppLog.InfoEvent(new BppLogEvent(BppLogFeatureScope.Android, "android.ui.injected"));
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.ui.inject_failed"),
                exception
            );
        }
        return _registered;
    }

    private void Update()
    {
        if (!_coroutineProbed)
        {
            _coroutineProbed = true;
            MelonCoroutines.Start(ProbeCoroutine(Time.frameCount));
        }
        Frame?.Invoke();
    }

    private void OnDestroy() =>
        BppLog.InfoEvent(new BppLogEvent(BppLogFeatureScope.Android, "android.ui.root_destroyed"));

    // A managed iterator cannot go to Unity's StartCoroutine, which takes an
    // Il2CppSystem.Collections.IEnumerator; MelonCoroutines runs it from the loader's own loop.
    // The probe records that both a frame yield and a Unity yield instruction resume it.
    private static IEnumerator ProbeCoroutine(int startFrame)
    {
        yield return null;
        var afterFrame = Time.frameCount;
        yield return new WaitForSeconds(0.5f);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.ui.coroutine_probe"),
            ("runner", "melon_coroutines"),
            ("start_frame", startFrame),
            ("after_yield_null_frame", afterFrame),
            ("after_wait_frame", Time.frameCount)
        );
    }
}
