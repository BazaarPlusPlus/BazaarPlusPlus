#nullable enable
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Supporters.Ui;

// The supporter line of a Desktop panel, drawn with the panel's kit: the names in their tier
// colours and the sponsor button.
internal sealed class BPPSupporterNativeAttributionRow
{
    private static readonly Color Muted = InGameTheme.InkMuted;
    private readonly DesktopKit _kit;
    private readonly RectTransform _names;
    private readonly TextMeshProUGUI _actionLabel;
    private readonly bool _stacked;
    private IReadOnlyList<BPPSupporterSample>? _samples;
    private string? _language;

    internal BPPSupporterNativeAttributionRow(
        RectTransform parent,
        DesktopKit kit,
        TextAnchor alignment = TextAnchor.MiddleLeft,
        bool stacked = false
    )
    {
        _kit = kit;
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
        var action = kit.Button(
            parent,
            string.Empty,
            stacked ? KitButtonKind.Secondary : KitButtonKind.Quiet,
            KitSize.Sm,
            () => Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(Language()))
        );
        action.name = "SupporterAction";
        _actionLabel = action.GetComponentInChildren<TextMeshProUGUI>(true);
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
                Name("·", InGameTheme.WithAlpha(Muted, .55f));
            var supporter = supporters[i];
            var color = BPPSupporterTierColors.ForRow(supporter.Tier);
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
        var text = _kit.Text(
            parent,
            value,
            size >= 16 ? KitTextRole.Body : KitTextRole.Meta,
            color
        );
        text.name = "AttributionText";
        text.richText = false;
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
