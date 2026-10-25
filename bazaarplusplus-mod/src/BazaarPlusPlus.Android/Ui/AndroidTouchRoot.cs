using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The touch UI's managed <c>MonoBehaviour</c>, registered with the IL2CPP runtime through
/// <see cref="ClassInjector"/> and mounted on the touch UI's root. Unity calls its
/// <c>Update</c> every frame while the root lives, which drives the touch UI; destroying the root
/// ends that.
/// </summary>
internal sealed class AndroidTouchRoot : MonoBehaviour
{
    private static bool _registered;

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

    private void Update() => Frame?.Invoke();

    private void OnDestroy() =>
        BppLog.InfoEvent(new BppLogEvent(BppLogFeatureScope.Android, "android.ui.root_destroyed"));
}
