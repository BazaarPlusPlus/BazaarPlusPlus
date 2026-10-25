using BazaarPlusPlus.Android.GameInterop;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>Scrollable native tooltip text; long effects stay readable at the body text floor.</summary>
internal sealed class AndroidInspectionText
{
    private readonly ScrollRect _scroll;
    private readonly RectTransform _content;
    private readonly TextMeshProUGUI _text;
    private readonly RectTransform _area;

    public AndroidInspectionText(RectTransform area, TMP_FontAsset font, float size, Color color)
    {
        _area = area;
        (_scroll, _content) = AndroidUiKit.VerticalScroll(area);
        var rect = AndroidUiKit.Rect(
            "Text",
            _content,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        _text = AndroidUiKit.Text(rect, font, "", size, TextAlignmentOptions.TopLeft, color);
        _text.overflowMode = TextOverflowModes.Overflow;
    }

    public void Set(string text)
    {
        _text.text = text;
        AndroidKeywordSprites.Apply(_text);
        _content.sizeDelta = new Vector2(
            0f,
            Mathf.Max(_area.rect.height, _text.GetPreferredValues(text, _area.rect.width, 0f).y)
        );
        _scroll.StopMovement();
        _content.anchoredPosition = Vector2.zero;
    }
}
