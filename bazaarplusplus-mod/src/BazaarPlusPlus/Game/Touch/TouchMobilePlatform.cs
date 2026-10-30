using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Mobile;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mono host's Mobile Platform Port (ADR-0025), installed on a mobile platform only. Unity's
/// own synchronization context already carries the main thread, so the main-thread scope is empty.
/// </summary>
internal sealed class TouchMobilePlatform : IMobilePlatformPort
{
    public IDisposable EnterMainThread() => EmptyScope.Instance;

    public RectTransform Rect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    ) => TouchUiKit.Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);

    public TextMeshProUGUI Text(
        RectTransform rect,
        TMP_FontAsset font,
        string text,
        float size,
        TextAlignmentOptions alignment,
        Color color
    ) => TouchUiKit.Text(rect, font, text, size, alignment, color);

    public (ScrollRect Scroll, RectTransform Content) VerticalScroll(RectTransform area) =>
        TouchUiKit.VerticalScroll(area);

    public void ApplyKeywordSprites(TMP_Text label) => TouchKeywordSprites.Apply(label);

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose() { }
    }
}
