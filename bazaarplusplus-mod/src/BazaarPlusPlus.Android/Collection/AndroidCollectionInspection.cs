using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The tapped card, enlarged over the grid in the shared tap-to-inspect sheet
/// (<see cref="TouchInspectLayout"/>), with the tiers it can reach and the enchantments it takes.
/// A chip sets the same rented card up again at that tier or enchantment. The desktop's
/// inspection locks the game's own tooltip beside the card; Android has no hover tooltip to lock,
/// so the card's name stands beside it, over one drag-scrolled column of the chips and the card's
/// text as the game renders it for the chosen tier and enchantment
/// (<see cref="AndroidCardTemplateText.Description(CardRentalSubject)"/>, as History and Ten-Win
/// Builds show it). A tap outside the info column, the close button, or the back key dismisses it.
/// </summary>
internal sealed class AndroidCollectionInspection
{
    public const string RentalOwner = "collection.inspection";

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly ScrollRect _detailScroll;
    private readonly RectTransform _detail;
    private readonly TextMeshProUGUI _name;
    private readonly TextMeshProUGUI _probe;
    private readonly TMP_FontAsset _font;
    private readonly CollectionTouchLayout _layout;
    private CollectionCardVm? _card;
    private CardRentalLease<CardPreviewBase>? _lease;
    private CardTier _tier;
    private CardEnchantment? _enchantment;
    private int _columns;
    private string _text = string.Empty;

    public AndroidCollectionInspection(
        RectTransform parent,
        TMP_FontAsset font,
        CollectionTouchLayout layout
    )
    {
        _font = font;
        _layout = layout;
        var inspect = layout.Inspect;
        var chrome = layout.Chrome;
        var root = AndroidUiKit.Rect(
            "Inspection",
            parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        AndroidUiKit.Fill(root, new Color(0f, 0f, 0f, 0.85f));
        var dismiss = root.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener((UnityEngine.Events.UnityAction)(() => Close("outside_tap")));
        _root = root.gameObject;

        var area = AndroidTouchScreen.Child("CardArea", root, inspect.CardArea);
        _slot = AndroidUiKit.Rect(
            "CardSlot",
            area,
            new(0.5f, 0.5f),
            new(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );

        var info = AndroidTouchScreen.Child("Info", root, inspect.Info);
        // Opaque, so the grid under it does not show through.
        var panel = AndroidUiKit.PanelColor;
        panel.a = 1f;
        AndroidUiKit.Fill(info, panel);
        // Swallows taps on the column, which would otherwise reach the dismiss button behind it.
        var swallow = info.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        var origin = inspect.Info;
        _name = AndroidUiKit.Text(
            AndroidTouchScreen.Child("Name", info, Local(inspect.Name, origin, chrome.Gap)),
            font,
            "",
            chrome.NameFont,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        _name.overflowMode = TextOverflowModes.Ellipsis;
        var close = AndroidTouchScreen.Child("Close", info, Local(inspect.Close, origin, 0f));
        AndroidUiKit.CloseButton(close, font, chrome.TitleFont, () => Close("close_button"));
        var detail = AndroidTouchScreen.Child(
            "Detail",
            info,
            Local(inspect.Detail, origin, chrome.Gap)
        );
        (_detailScroll, _detail) = AndroidUiKit.VerticalScroll(detail);
        // Measures chip labels at body size, so a row holds only as many chips as show whole.
        _probe = AndroidUiKit.Text(
            AndroidUiKit.Fixed("ChipProbe", info, Vector2.zero, Vector2.zero, Vector2.zero),
            font,
            "",
            chrome.BodyFont,
            TextAlignmentOptions.Left,
            Color.clear
        );
        _probe.enableWordWrapping = false;
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    /// <summary>The detail column's scroll view, for the panel's drag-scroll log.</summary>
    public ScrollRect DetailScroll => _detailScroll;

    public void Open(CollectionCardVm card)
    {
        Return();
        _card = card;
        _tier = card.StartingTier;
        _enchantment = null;
        _text = AndroidCardTemplateText.Description(Subject()) ?? card.Description;
        _name.text = card.DisplayName;
        var bounds = AndroidCollectionGrid.NaturalBounds(card);
        var widthScale =
            card.Type == CardType.Item ? CollectionGridConstants.ItemCardWidthScale : 1f;
        var scale = _layout.InspectScale(bounds.Width * widthScale, bounds.Height);
        _slot.localScale = new Vector3(scale * widthScale, scale, 1f);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _lease = AndroidCardRental.Rent(Subject(), _slot, RentalOwner);
        RenderDetail();
        _detailScroll.StopMovement();
        _detail.anchoredPosition = Vector2.zero;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspected"),
            ("type", card.Type),
            ("size", card.Size),
            ("tier", _tier),
            ("enchantable", card.IsEnchantable),
            ("scale", Math.Round(scale, 3)),
            ("chip_columns", _columns)
        );
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        Return();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspection_closed"),
            ("reason", reason),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
        return true;
    }

    private void Return()
    {
        AndroidCardRental.Return(_lease);
        _lease = null;
        _card = null;
    }

    private CardRentalSubject Subject() => new(_card!.Id, _tier, _enchantment);

    private void Select(CardTier tier, CardEnchantment? enchantment)
    {
        if (_card == null || _lease == null || (tier == _tier && enchantment == _enchantment))
            return;
        _tier = tier;
        _enchantment = enchantment;
        AndroidCardRental.Retarget(_lease, Subject());
        // The values follow the chosen tier and enchantment, as the retargeted card's do.
        _text = AndroidCardTemplateText.Description(Subject()) ?? _card.Description;
        RenderDetail();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspection_retargeted"),
            ("tier", tier),
            ("enchantment", enchantment)
        );
    }

    // The tier chips, the enchantment chips, then the card's text, as one scrolled column.
    private void RenderDetail()
    {
        for (var index = _detail.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_detail.GetChild(index).gameObject);
        var card = _card;
        if (card == null)
            return;
        var chrome = _layout.Chrome;
        var tiers = ReachableTiers(card.StartingTier)
            .Select(tier => (Label: CollectionPanelText.Tier(tier), Tier: tier))
            .ToList();
        var enchantments =
            card.IsEnchantable && card.Enchantments.Count > 0
                ? card
                    .Enchantments.Keys.OrderBy(value => value)
                    .Select(value =>
                        (Label: AndroidCollectionLabels.Enchantment(value), Value: value)
                    )
                    .ToList()
                : new List<(string Label, CardEnchantment Value)>();
        var widest = tiers
            .Select(tier => tier.Label)
            .Concat(enchantments.Select(enchantment => enchantment.Label))
            .Select(LabelWidth)
            .DefaultIfEmpty(0f)
            .Max();
        _columns = _layout.InspectChipColumnsFor(widest + 2f * AndroidUiKit.ButtonLabelInset);
        var flow = new AndroidChipFlow(_detail, _font, chrome, _columns, _layout.RailHeaderHeight);
        foreach (var (label, tier) in tiers)
            flow.Chip(label, tier == _tier, () => Select(tier, _enchantment));
        if (enchantments.Count > 0)
        {
            flow.Break();
            flow.Chip("-", _enchantment == null, () => Select(_tier, null));
            foreach (var (label, enchantment) in enchantments)
                flow.Chip(label, enchantment == _enchantment, () => Select(_tier, enchantment));
        }
        if (!string.IsNullOrWhiteSpace(_text))
            AndroidKeywordSprites.Apply(flow.Note(_text, AndroidUiKit.MutedTextColor));
        _detail.sizeDelta = new Vector2(0f, flow.Height + chrome.Gap);
    }

    // A label's width at body size, the smallest a chip's autosize goes.
    private float LabelWidth(string label) => _probe.GetPreferredValues(label).x;

    // A card upgrades from its starting tier to Diamond; a Legendary card stays Legendary.
    private static IEnumerable<CardTier> ReachableTiers(CardTier starting)
    {
        if (starting == CardTier.Legendary)
        {
            yield return CardTier.Legendary;
            yield break;
        }
        for (var tier = starting; tier <= CardTier.Diamond; tier++)
            yield return tier;
    }

    // A sheet rect as its parent's local rect, inset horizontally for text.
    private static TouchRect Local(TouchRect rect, TouchRect origin, float inset) =>
        new(
            rect.Left - origin.Left + inset,
            rect.Top - origin.Top,
            Math.Max(0f, rect.Width - inset * 2f),
            rect.Height
        );
}
