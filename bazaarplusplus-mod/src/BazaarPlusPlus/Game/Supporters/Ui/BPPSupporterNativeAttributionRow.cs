#nullable enable
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Supporters.Ui;

/// <summary>
/// The supporter line of every Desktop panel, drawn with the panel's kit: one row under the
/// panel's title, "Thanks", up to four names in their tier colours, and the sponsor link at
/// the row's right edge. Names give way first when the row is narrow.
/// </summary>
internal sealed class BPPSupporterNativeAttributionRow
{
    private const int Names = 4;

    private readonly DesktopKit _kit;
    private readonly TextMeshProUGUI _prefix;
    private readonly RectTransform _names;
    private readonly TextMeshProUGUI _action;
    private IReadOnlyList<BPPSupporterSample>? _samples;
    private string? _language;

    internal BPPSupporterNativeAttributionRow(Transform parent, DesktopKit kit)
    {
        _kit = kit;
        var row = DesktopKit.Rect(parent, "SupporterLine");
        var group = Line(row, InGameSizes.Space8);
        // The sponsor link has no plate at rest: its label sits on the row's right edge.
        group.padding = new RectOffset(0, -(int)DesktopKit.LabelInset(KitSize.Sm), 0, 0);
        _prefix = kit.Text(row, string.Empty, KitTextRole.Caption);
        _names = DesktopKit.Rect(row, "Names");
        Line(_names, InGameSizes.Space8);
        // The names take what the word and the link leave, and show only the names that fit.
        var room = _names.gameObject.AddComponent<LayoutElement>();
        room.minWidth = room.preferredWidth = 0;
        room.flexibleWidth = 1;
        _names.gameObject.AddComponent<WholeNames>();
        var sponsor = kit.Button(
            row,
            string.Empty,
            KitButtonKind.Quiet,
            KitSize.Sm,
            () => Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(Language()))
        );
        sponsor.name = "SponsorLink";
        sponsor.InkOverride = InGameTheme.Supporter4;
        _action = sponsor.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    internal void Bind(IReadOnlyList<BPPSupporterSample> samples)
    {
        var language = Language();
        if (ReferenceEquals(samples, _samples) && language == _language)
            return;
        _samples = samples;
        _language = language;
        _prefix.text = BPPSupporterAttributionText.FormatThanks(language);
        _action.text = BPPSupporterAttributionText.FormatSponsorAction(language);
        foreach (Transform child in _names)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        var supporters = samples.Where(sample => sample.HasValue).Take(Names).ToArray();
        if (supporters.Length == 0)
        {
            Name(BPPSupporterAttributionText.FormatCommunity(language), InGameTheme.InkMuted);
            return;
        }
        foreach (var supporter in supporters)
            Name(supporter.Name, BPPSupporterTierColors.For(supporter.Tier));
    }

    private void Name(string value, Color color)
    {
        var text = _kit.Text(_names, value, KitTextRole.Caption, color);
        text.richText = false;
    }

    // Shows the names that fit whole, from the first: a row of cut names reads as noise. The
    // first name always shows, with an ellipsis if it alone is too long.
    private sealed class WholeNames : MonoBehaviour
    {
        private readonly Dictionary<Transform, float> _widths = new();

        private void LateUpdate()
        {
            var row = (RectTransform)transform;
            var spacing = GetComponent<HorizontalLayoutGroup>().spacing;
            var used = 0f;
            var full = false;
            for (var i = 0; i < row.childCount; i++)
            {
                var child = (RectTransform)row.GetChild(i);
                if (child.gameObject.activeSelf || !_widths.ContainsKey(child))
                    _widths[child] = LayoutUtility.GetPreferredWidth(child);
                var next = used + (i > 0 ? spacing : 0) + _widths[child];
                full = full || (i > 0 && next > row.rect.width + .5f);
                if (!full)
                    used = next;
                if (child.gameObject.activeSelf == full)
                    child.gameObject.SetActive(!full);
            }
        }
    }

    private static HorizontalLayoutGroup Line(RectTransform rect, float spacing)
    {
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        return group;
    }

    private static string Language() => PlayerPreferences.Data.LanguageCode ?? string.Empty;
}
