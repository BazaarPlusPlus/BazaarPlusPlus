using BazaarPlusPlus.Android.GameInterop;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>Borrowed game art, with independent controls and no native business callbacks.</summary>
internal sealed class AndroidNativeSkin
{
    private readonly AndroidNativeArt _art = new();

    public bool NeedsRefresh => _art.NeedsRefresh;

    public void Frame(RectTransform rect, Color? backing = null)
    {
        var background = backing ?? AndroidUiKit.PanelColor;
        background.a = 1f;
        AndroidUiKit.Fill(rect, background);
        DrawFrame(rect, fillCenter: true);
    }

    private void DrawFrame(RectTransform rect, bool fillCenter)
    {
        if (_art.Frame == null)
            return;
        var art = AndroidUiKit.Rect(
            "FrameArt",
            rect,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var image = AndroidUiKit.Fill(art, Color.white, false);
        image.sprite = _art.Frame;
        image.type = Image.Type.Sliced;
        image.fillCenter = fillCenter;
        image.color = Color.white;
    }

    public Image Tag(RectTransform rect)
    {
        var image = AndroidUiKit.Fill(rect, AndroidUiKit.ButtonColor, false);
        image.sprite = _art.TagFrame;
        image.type = Image.Type.Sliced;
        return image;
    }

    public void Toggle(
        RectTransform rect,
        TMP_FontAsset font,
        string label,
        float fontSize,
        float trackHeight,
        bool value,
        Action<bool> onChanged
    )
    {
        var hitArea = AndroidUiKit.Fill(rect, new Color(0f, 0f, 0f, 0.001f));
        var height = trackHeight;
        var width = height * 1.75f;
        var track = AndroidUiKit.Fixed(
            "Track",
            rect,
            new Vector2(1f, 0.5f),
            Vector2.zero,
            new Vector2(width, height)
        );
        var background = AndroidUiKit.Fill(
            track,
            value ? AndroidUiKit.ActiveColor : AndroidUiKit.ButtonColor
        );
        background.sprite = _art.TagFrame;
        background.type = Image.Type.Sliced;
        var padding = height * 0.12f;
        var diameter = height - 2f * padding;
        var thumb = AndroidUiKit.Fixed(
            "Thumb",
            track,
            new Vector2(value ? 1f : 0f, 0.5f),
            new Vector2(value ? -padding : padding, 0f),
            new Vector2(diameter, diameter)
        );
        var thumbImage = AndroidUiKit.Fill(thumb, AndroidUiKit.TextColor, false);
        thumbImage.sprite = _art.TagFrame;
        thumbImage.type = Image.Type.Sliced;
        var toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = hitArea;
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener((UnityAction<bool>)onChanged);
        AndroidUiKit.Text(
            AndroidUiKit.Rect(
                "Label",
                rect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                new Vector2(-width - fontSize, 0f)
            ),
            font,
            label,
            fontSize,
            TextAlignmentOptions.Left,
            AndroidUiKit.TextColor
        );
    }

    public (Button Button, Image Background, TextMeshProUGUI Label) Button(
        RectTransform rect,
        TMP_FontAsset font,
        string label,
        float size,
        float inset,
        Action onTap
    )
    {
        var result = AndroidUiKit.Button(rect, font, label, size, inset, onTap);
        Select(result.Background, false);
        return result;
    }

    public void Select(Image image, bool selected, bool dimmed = false)
    {
        image.sprite = selected && _art.Selected != null ? _art.Selected : _art.Button;
        image.type = Image.Type.Sliced;
        image.color =
            image.sprite == null
                ? selected
                    ? AndroidUiKit.ActiveColor
                    : AndroidUiKit.ButtonColor
                : dimmed
                    ? new Color(0.48f, 0.43f, 0.36f)
                    : selected
                        ? new Color(1f, 0.88f, 0.62f)
                        : Color.white;
    }
}
