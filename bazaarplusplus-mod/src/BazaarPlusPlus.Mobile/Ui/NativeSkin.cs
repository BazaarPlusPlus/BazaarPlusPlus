using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

internal sealed class NativeSkin
{
    private readonly NativeSkinArt _art = MobilePlatform.Port.ReadNativeSkinArt();
    private readonly Sprite? _plate;
    private readonly Sprite? _selectedPlate;
    private readonly float _plateMultiplier;

    public NativeSkin() { }

    public NativeSkin(TouchPlates? plates, float multiplier)
    {
        _plate = plates?.HeaderNormal;
        _selectedPlate = plates?.HeaderSelected;
        _plateMultiplier = multiplier;
    }

    public bool NeedsRefresh
    {
        get
        {
            return !Il2CppUi.IsAlive(_art.Button)
                || !Il2CppUi.IsAlive(_art.Frame)
                || !Il2CppUi.IsAlive(_art.TagFrame);
        }
    }

    // Use borderOnly when the backing supplies the interior so it is filled once.
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
        var hitArea = UiKit.HitArea(rect);
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
        Il2CppUi.OnValueChanged(toggle, onChanged);
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
        if (_plate != null && _selectedPlate != null)
        {
            // The selected plate carries its own gold rim, so it takes no tint.
            image.sprite = selected ? _selectedPlate : _plate;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = _plateMultiplier;
            image.color = dimmed ? InGameTheme.ArtPanelTint : Color.white;
            return;
        }
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
