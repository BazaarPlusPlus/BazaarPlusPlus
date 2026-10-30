using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Mobile;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android host's Mobile Platform Port (ADR-0025): the main-thread scope is the host's own
/// pump (<see cref="AndroidMainThread"/>), since IL2CPP installs no synchronization context; a
/// Unity object is alive only while its IL2CPP proxy is not collected; casts and listeners go
/// through Il2CppInterop; the native skin's sprites come from <see cref="AndroidNativeArt"/>.
/// </summary>
internal sealed class AndroidMobilePlatform : IMobilePlatformPort
{
    public IDisposable EnterMainThread() => AndroidMainThread.Host.Enter();

    public bool IsAlive(UnityEngine.Object? value) => value != null && !value.WasCollected;

    public T Cast<T>(UnityEngine.Object value)
        where T : UnityEngine.Object => value.Cast<T>();

    public void SetWordWrapping(TMP_Text label, bool wrap) => label.enableWordWrapping = wrap;

    public void OnClick(Button button, Action onTap) =>
        button.onClick.AddListener((UnityAction)onTap);

    public void OnValueChanged(Toggle toggle, Action<bool> onChanged) =>
        toggle.onValueChanged.AddListener((UnityAction<bool>)onChanged);

    public NativeSkinArt ReadNativeSkinArt()
    {
        var art = new AndroidNativeArt();
        return new NativeSkinArt(art.Button, art.Selected, art.Frame, art.TagFrame);
    }
}
