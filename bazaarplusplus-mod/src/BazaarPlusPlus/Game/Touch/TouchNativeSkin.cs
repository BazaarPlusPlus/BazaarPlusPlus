#nullable enable
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>Borrowed game art, with independent controls and no native business callbacks.</summary>
internal sealed class TouchNativeSkin
{
    private readonly TouchNativeArt _art = new();

    public bool NeedsRefresh => _art.NeedsRefresh;

    /// <summary>
    /// An opaque backing with the native frame art over it. With <paramref name="borderOnly"/>
    /// the art's centre is not drawn: the backing is the interior, so the rect fills its interior
    /// once, not twice. History opts in; the other panels keep the filled centre until device
    /// screenshots confirm the art's centre adds nothing over the backing.
    /// </summary>
    public void Frame(RectTransform rect, Color? backing = null, bool borderOnly = false)
    {
        var background = backing ?? TouchUiKit.PanelColor;
        background.a = 1f;
        TouchUiKit.Fill(rect, background);
        DrawFrame(rect, fillCenter: !borderOnly);
    }

    private void DrawFrame(RectTransform rect, bool fillCenter)
    {
        if (_art.Frame == null)
            return;
        var art = TouchUiKit.Rect(
            "FrameArt",
            rect,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var image = TouchUiKit.Fill(art, Color.white, false);
        image.sprite = _art.Frame;
        image.type = Image.Type.Sliced;
        image.fillCenter = fillCenter;
        image.color = Color.white;
    }

    public Image Tag(RectTransform rect)
    {
        var image = TouchUiKit.Fill(rect, TouchUiKit.ButtonColor, false);
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
        var hitArea = TouchUiKit.Fill(rect, new Color(0f, 0f, 0f, 0.001f));
        var height = trackHeight;
        var width = height * 1.75f;
        var track = TouchUiKit.Fixed(
            "Track",
            rect,
            new Vector2(1f, 0.5f),
            Vector2.zero,
            new Vector2(width, height)
        );
        var background = TouchUiKit.Fill(
            track,
            value ? TouchUiKit.ActiveColor : TouchUiKit.ButtonColor
        );
        background.sprite = _art.TagFrame;
        background.type = Image.Type.Sliced;
        var padding = height * 0.12f;
        var diameter = height - 2f * padding;
        var thumb = TouchUiKit.Fixed(
            "Thumb",
            track,
            new Vector2(value ? 1f : 0f, 0.5f),
            new Vector2(value ? -padding : padding, 0f),
            new Vector2(diameter, diameter)
        );
        var thumbImage = TouchUiKit.Fill(thumb, TouchUiKit.TextColor, false);
        thumbImage.sprite = _art.TagFrame;
        thumbImage.type = Image.Type.Sliced;
        var toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = hitArea;
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(new UnityAction<bool>(onChanged));
        TouchUiKit.Text(
            TouchUiKit.Rect(
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
            TouchUiKit.TextColor
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
        var result = TouchUiKit.Button(rect, font, label, size, inset, onTap);
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
                    ? TouchUiKit.ActiveColor
                    : TouchUiKit.ButtonColor
                : dimmed
                    ? InGameTheme.ArtPanelTint
                    : selected
                        ? InGameTheme.ArtSelectedTint
                        : Color.white;
    }
}
