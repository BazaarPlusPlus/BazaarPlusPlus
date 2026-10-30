using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Mobile;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android host's Mobile Platform Port (ADR-0025): the main-thread scope is the host's own
/// pump (<see cref="AndroidMainThread"/>), since IL2CPP installs no synchronization context.
/// </summary>
internal sealed class AndroidMobilePlatform : IMobilePlatformPort
{
    public IDisposable EnterMainThread() => AndroidMainThread.Host.Enter();

    public RectTransform Rect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    ) => AndroidUiKit.Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);

    public TextMeshProUGUI Text(
        RectTransform rect,
        TMP_FontAsset font,
        string text,
        float size,
        TextAlignmentOptions alignment,
        Color color
    ) => AndroidUiKit.Text(rect, font, text, size, alignment, color);

    public (ScrollRect Scroll, RectTransform Content) VerticalScroll(RectTransform area) =>
        AndroidUiKit.VerticalScroll(area);

    public void ApplyKeywordSprites(TMP_Text label) => AndroidKeywordSprites.Apply(label);
}
