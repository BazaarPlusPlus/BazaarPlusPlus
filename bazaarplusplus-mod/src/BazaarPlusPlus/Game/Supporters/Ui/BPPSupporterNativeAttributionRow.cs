#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Supporters.Ui;

// Native attribution shared by the archive and live build panels.
internal sealed class BPPSupporterNativeAttributionRow
{
    private static readonly Color Muted = new(.69f, .63f, .53f);
    private readonly NativeGameTypography.OwnedTextPreparation _font;
    private readonly RectTransform _names;
    private readonly TextMeshProUGUI _actionLabel;
    private readonly Image _actionBackground;
    private readonly Image _actionFrame;
    private readonly bool _stacked;
    private IReadOnlyList<BPPSupporterSample>? _samples;
    private string? _language;

    internal BPPSupporterNativeAttributionRow(
        RectTransform parent,
        NativeGameTypography.OwnedTextPreparation font,
        TextAnchor alignment = TextAnchor.MiddleLeft,
        bool stacked = false
    )
    {
        _font = font;
        _stacked = stacked;
        HorizontalOrVerticalLayoutGroup layout = stacked
            ? parent.gameObject.AddComponent<VerticalLayoutGroup>()
            : parent.gameObject.AddComponent<HorizontalLayoutGroup>();
        Configure(layout, alignment, 10);
        _names = Child("SupporterNames", parent);
        HorizontalOrVerticalLayoutGroup namesLayout = stacked
            ? _names.gameObject.AddComponent<VerticalLayoutGroup>()
            : _names.gameObject.AddComponent<HorizontalLayoutGroup>();
        Configure(namesLayout, alignment, 5);
        var action = Child("SupporterAction", parent);
        var size = action.gameObject.AddComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = 78;
        size.minHeight = size.preferredHeight = stacked ? 32 : 25;
        _actionBackground = action.gameObject.AddComponent<Image>();
        _actionBackground.type = Image.Type.Sliced;
        _actionBackground.pixelsPerUnitMultiplier = 2.5f;
        _actionFrame = Child("NativeFrame", action).gameObject.AddComponent<Image>();
        _actionFrame.type = Image.Type.Sliced;
        _actionFrame.pixelsPerUnitMultiplier = 2.5f;
        _actionFrame.raycastTarget = false;
        var button = action.gameObject.AddComponent<Button>();
        button.targetGraphic = _actionBackground;
        button.onClick.AddListener(() =>
            Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(Language()))
        );
        _actionLabel = Label(action, string.Empty, Colors.SupporterTier4Text);
        _actionLabel.alignment = TextAlignmentOptions.Center;
        SetSkin(null, null);
    }

    internal void SetSkin(Sprite? frame, Sprite? background)
    {
        _actionFrame.sprite = frame;
        _actionFrame.color = frame == null ? Color.clear : Color.white;
        _actionBackground.sprite = background;
        _actionBackground.color = background == null ? new Color(.18f, .14f, .1f) : Color.white;
    }

    internal void Bind(IReadOnlyList<BPPSupporterSample> samples, string fallback)
    {
        var language = Language();
        if (ReferenceEquals(samples, _samples) && language == _language)
            return;
        _samples = samples;
        _language = language;
        _actionLabel.text = BPPSupporterAttributionText.FormatSponsorAction(language);
        foreach (Transform child in _names)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        var supporters = samples.Where(sample => sample.HasValue).Take(4).ToArray();
        if (supporters.Length == 0)
        {
            Name(fallback, Muted, shrink: true, maxWidth: 450);
            return;
        }
        Name(BPPSupporterAttributionText.FormatSupportedByPrefix(language), Muted);
        for (var i = 0; i < supporters.Length; i++)
        {
            if (i > 0 && !_stacked)
                Name("·", new Color(Muted.r, Muted.g, Muted.b, .55f));
            var supporter = supporters[i];
            var color = supporter.Tier switch
            {
                >= 4 => Colors.SupporterTier4Text,
                3 => Colors.SupporterTier3Text,
                2 => Colors.SupporterTier2Text,
                _ => Colors.SupporterTier1Text,
            };
            var text = Name(supporter.Name, color, shrink: true);
            if (supporter.Tier >= 2)
            {
                var highlight = Color.Lerp(color, Color.white, .55f);
                text.color = Color.white;
                text.enableVertexGradient = true;
                text.colorGradient = new VertexGradient(highlight, highlight, color, color);
            }
        }
        var suffix = BPPSupporterAttributionText.FormatSupportedBySuffix(language);
        if (!string.IsNullOrWhiteSpace(suffix))
            Name(suffix, Muted);
    }

    private TextMeshProUGUI Name(
        string value,
        Color color,
        bool shrink = false,
        float maxWidth = 110
    )
    {
        var text = Label(_names, value, color, _stacked ? 16 : 14);
        var width = text.GetPreferredValues(value).x + 2;
        var layout = text.gameObject.AddComponent<LayoutElement>();
        layout.minWidth =
            _stacked ? 0
            : shrink ? Mathf.Min(width, 24)
            : width;
        layout.preferredWidth =
            _stacked ? width
            : shrink ? Mathf.Min(width, maxWidth)
            : width;
        layout.minHeight = layout.preferredHeight = _stacked ? 28 : 24;
        layout.flexibleWidth = 0;
        return text;
    }

    private TextMeshProUGUI Label(Transform parent, string value, Color color, int size = 14)
    {
        var text = Child("AttributionText", parent).gameObject.AddComponent<TextMeshProUGUI>();
        _font.Apply(text);
        text.text = value;
        text.richText = false;
        text.fontStyle = FontStyles.Normal;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void Configure(
        HorizontalOrVerticalLayoutGroup layout,
        TextAnchor alignment,
        float spacing
    )
    {
        layout.childAlignment = alignment;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout is VerticalLayoutGroup;
        layout.childForceExpandHeight = layout is HorizontalLayoutGroup;
        layout.spacing = spacing;
    }

    private static string Language() => PlayerPreferences.Data.LanguageCode ?? string.Empty;
}
