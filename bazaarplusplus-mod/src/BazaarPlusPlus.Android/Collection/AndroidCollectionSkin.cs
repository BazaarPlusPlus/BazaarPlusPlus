using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>Borrowed game art, with independent controls and no native business callbacks.</summary>
internal sealed class AndroidCollectionSkin
{
    private Sprite? _button;
    private Sprite? _selected;
    private Sprite? _frame;
    private Sprite? _tagFrame;

    public AndroidCollectionSkin()
    {
        try
        {
            foreach (var popup in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.GenericPopup>())
            {
                if (popup._buttons == null)
                    continue;
                foreach (var button in popup._buttons)
                {
                    if (button == null || button.DefaultImage == null)
                        continue;
                    _button = button.DefaultImage;
                    _selected = button.ClickedImage;
                    break;
                }
                if (_button != null)
                    break;
            }
            foreach (
                var tooltip in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.UI.Tooltips.BaseTooltipController>()
            )
            {
                var frame = tooltip.bronzeFrameSettings?.BackgroundFrame;
                if (frame == null)
                    continue;
                _frame = frame;
                _tagFrame = tooltip.bronzeFrameSettings?.TagFrame;
                break;
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.skin_failed"),
                exception
            );
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.skin_loaded"),
            ("button", _button != null),
            ("frame", _frame != null),
            ("tag_frame", _tagFrame != null)
        );
    }

    public bool NeedsRefresh => _button == null || _frame == null || _tagFrame == null;

    public void Frame(RectTransform rect)
    {
        var background = AndroidUiKit.PanelColor;
        background.a = 1f;
        AndroidUiKit.Fill(rect, background);
        if (_frame == null)
            return;
        // Native frame art has a transparent centre; keep an opaque independent backing.
        var art = AndroidUiKit.Rect(
            "FrameArt",
            rect,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var image = AndroidUiKit.Fill(art, Color.white, false);
        image.sprite = _frame;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
    }

    public void Tag(RectTransform rect)
    {
        if (_tagFrame == null)
            return;
        var image = AndroidUiKit.Fill(rect, AndroidUiKit.ButtonColor, false);
        image.sprite = _tagFrame;
        image.type = Image.Type.Sliced;
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
        background.sprite = _tagFrame;
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
        thumbImage.sprite = _tagFrame;
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

    public void Select(Image image, bool selected)
    {
        image.sprite = selected && _selected != null ? _selected : _button;
        image.type = Image.Type.Sliced;
        image.color =
            image.sprite == null
                ? selected
                    ? AndroidUiKit.ActiveColor
                    : AndroidUiKit.ButtonColor
                : selected
                    ? new Color(1f, 0.88f, 0.62f)
                    : Color.white;
    }
}
