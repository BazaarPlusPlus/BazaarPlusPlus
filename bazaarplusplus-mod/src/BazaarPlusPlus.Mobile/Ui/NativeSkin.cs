using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

/// <summary>Borrowed game art, with independent controls and no native business callbacks.</summary>
internal sealed class NativeSkin
{
    private readonly NativeSkinArt _art = MobilePlatform.Port.ReadNativeSkinArt();

    /// <summary>The game destroyed or never loaded a sprite this skin draws with.</summary>
    public bool NeedsRefresh
    {
        get
        {
            var platform = MobilePlatform.Port;
            return !platform.IsAlive(_art.Button)
                || !platform.IsAlive(_art.Frame)
                || !platform.IsAlive(_art.TagFrame);
        }
    }

    /// <summary>
    /// An opaque backing with the native frame art over it. With <paramref name="borderOnly"/>
    /// the art's centre is not drawn: the backing is the interior, so the rect fills its interior
    /// once, not twice. History opts in; the other panels keep the filled centre until device
    /// screenshots confirm the art's centre adds nothing over the backing.
    /// </summary>
    public void Frame(RectTransform rect, Color? backing = null, bool borderOnly = false)
    {
        var background = backing ?? UiKit.PanelColor;
        background.a = 1f;
        UiKit.Fill(rect, background);
        DrawFrame(rect, fillCenter: !borderOnly);
    }

    private void DrawFrame(RectTransform rect, bool fillCenter)
    {
        if (_art.Frame == null)
            return;
        var art = UiKit.Rect(
            "FrameArt",
            rect,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var image = UiKit.Fill(art, Color.white, false);
        image.sprite = _art.Frame;
        image.type = Image.Type.Sliced;
        image.fillCenter = fillCenter;
        image.color = Color.white;
    }

    public Image Tag(RectTransform rect)
    {
        var image = UiKit.Fill(rect, UiKit.ButtonColor, false);
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
        var hitArea = UiKit.Fill(rect, new Color(0f, 0f, 0f, 0.001f));
        var height = trackHeight;
        var width = height * 1.75f;
        var track = UiKit.Fixed(
            "Track",
            rect,
            new Vector2(1f, 0.5f),
            Vector2.zero,
            new Vector2(width, height)
        );
        var background = UiKit.Fill(track, value ? UiKit.ActiveColor : UiKit.ButtonColor);
        background.sprite = _art.TagFrame;
        background.type = Image.Type.Sliced;
        var padding = height * 0.12f;
        var diameter = height - 2f * padding;
        var thumb = UiKit.Fixed(
            "Thumb",
            track,
            new Vector2(value ? 1f : 0f, 0.5f),
            new Vector2(value ? -padding : padding, 0f),
            new Vector2(diameter, diameter)
        );
        var thumbImage = UiKit.Fill(thumb, UiKit.TextColor, false);
        thumbImage.sprite = _art.TagFrame;
        thumbImage.type = Image.Type.Sliced;
        var toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = hitArea;
        toggle.SetIsOnWithoutNotify(value);
        MobilePlatform.Port.OnValueChanged(toggle, onChanged);
        UiKit.Text(
            UiKit.Rect(
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
            UiKit.TextColor
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
        var result = UiKit.Button(rect, font, label, size, inset, onTap);
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
                    ? UiKit.ActiveColor
                    : UiKit.ButtonColor
                : dimmed
                    ? InGameTheme.ArtPanelTint
                    : selected
                        ? InGameTheme.ArtSelectedTint
                        : Color.white;
    }
}
