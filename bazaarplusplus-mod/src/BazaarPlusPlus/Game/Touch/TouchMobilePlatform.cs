using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Mobile;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mono host's Mobile Platform Port (ADR-0025), installed on a mobile platform only. Unity's
/// own synchronization context already carries the main thread, so the main-thread scope is
/// empty; liveness, casts and listeners are plain Unity and C#; the native skin's sprites come
/// from <see cref="TouchNativeArt"/>.
/// </summary>
internal sealed class TouchMobilePlatform : IMobilePlatformPort
{
    public IDisposable EnterMainThread() => EmptyScope.Instance;

    public bool IsAlive(UnityEngine.Object? value) => value != null;

    public T Cast<T>(UnityEngine.Object value)
        where T : UnityEngine.Object => (T)value;

    public void SetWordWrapping(TMP_Text label, bool wrap) =>
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;

    public void OnClick(Button button, Action onTap) =>
        button.onClick.AddListener(new UnityAction(onTap));

    public void OnValueChanged(Toggle toggle, Action<bool> onChanged) =>
        toggle.onValueChanged.AddListener(new UnityAction<bool>(onChanged));

    public NativeSkinArt ReadNativeSkinArt()
    {
        var art = new TouchNativeArt();
        return new NativeSkinArt(art.Button, art.Selected, art.Frame, art.TagFrame);
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose() { }
    }
}
