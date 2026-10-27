using System.Globalization;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppTheBazaar.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The selected card and its tier comparison in the shared touch inspection frame.
/// A toggle expands all available enchantment effects using the in-game preview renderer.
/// The base card, cooldown and ammo remain above the list.
/// </summary>
internal sealed class AndroidCollectionInspection
{
    public const string RentalOwner = "collection.inspection";

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly CanvasGroup _cardOpacity;
    private readonly CanvasGroup _infoOpacity;
    private readonly CanvasGroup _detailInteraction;
    private readonly Image _backdrop;
    private readonly InspectionTransition _transition = new();
    private CanvasGroup? _sourceOpacity;
    private float _sourceAlpha;
    private Vector3 _sourcePosition;
    private Vector3 _sourceScale;
    private Vector3 _previewScale;
    private Vector3 _previewPosition;
    private bool _spatialMotion;
    private float _pendingSeconds;
    private string _dismissReason = "back_key";
    private readonly ScrollRect _detailScroll;
    private readonly RectTransform _detail;
    private readonly TextMeshProUGUI _name;
    private readonly TMP_FontAsset _font;
    private readonly CollectionTouchLayout _layout;
    private CollectionCardVm? _card;
    private CardRentalLease<CardPreviewBase>? _lease;
    private readonly AndroidNativeSkin _skin;
    private bool _showEnchantments;
    private List<(CardEnchantment Type, string Text)>? _enchantmentEffects;
    private string _text = string.Empty;
    private string _cooldown = string.Empty;
    private string _ammo = string.Empty;

    public AndroidCollectionInspection(
        RectTransform parent,
        TMP_FontAsset font,
        CollectionTouchLayout layout,
        AndroidNativeSkin skin
    )
    {
        _font = font;
        _skin = skin;
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
        _backdrop = AndroidUiKit.Fill(root, Color.clear);
        var dismiss = root.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener((UnityEngine.Events.UnityAction)(() => Dismiss("outside_tap")));
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

        _cardOpacity = _slot.gameObject.AddComponent<CanvasGroup>();
        var info = AndroidTouchScreen.Child("Info", root, inspect.Info);
        _infoOpacity = info.gameObject.AddComponent<CanvasGroup>();
        // Opaque, so the grid under it does not show through.
        _skin.Frame(info);
        // Swallows taps on the column, which would otherwise reach the dismiss button behind it.
        var swallow = info.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        var origin = inspect.Info;
        _name = AndroidUiKit.Text(
            AndroidTouchScreen.Child(
                "Name",
                info,
                Local(inspect.Name, origin, chrome.Padding * 2f)
            ),
            font,
            "",
            chrome.NameFont,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        _name.overflowMode = TextOverflowModes.Ellipsis;
        var close = AndroidTouchScreen.Child("Close", info, Local(inspect.Close, origin, 0f));
        close.anchoredPosition += new Vector2(-chrome.Padding * 2f, -chrome.Padding);
        _skin.Button(
            close,
            font,
            L.Resolve(new LocalizedTextSet("Back", "返回", "返回")),
            chrome.TitleFont,
            chrome.LabelInset,
            () => HandleBack()
        );
        var detail = AndroidTouchScreen.Child(
            "Detail",
            info,
            Local(inspect.Detail, origin, chrome.Padding * 2f)
        );
        _detailInteraction = detail.gameObject.AddComponent<CanvasGroup>();
        (_detailScroll, _detail) = AndroidUiKit.VerticalScroll(detail);
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    /// <summary>The inspected card is still setting up: a relayout waits for it.</summary>
    public bool SettingUp => _lease is { State: CardRentalLeaseState.Pending };

    /// <summary>The detail column's scroll view, for the panel's drag-scroll log.</summary>
    public ScrollRect DetailScroll => _detailScroll;

    public void Open(CollectionCardVm card, RectTransform? source)
    {
        Close("replaced");
        _card = card;
        _showEnchantments = false;
        _enchantmentEffects = null;
        UpdatePreview();
        _name.text = card.DisplayName;
        var bounds = AndroidCollectionGrid.NaturalBounds(card);
        var widthScale =
            card.Type == CardType.Item ? CollectionGridConstants.ItemCardWidthScale : 1f;
        var scale = _layout.InspectScale(bounds.Width * widthScale, bounds.Height);
        _previewScale = new Vector3(scale * widthScale, scale, 1f);
        _slot.anchoredPosition = Vector2.zero;
        _previewPosition = _slot.localPosition;
        _transition.Begin(AndroidMotion.AnimationsEnabled());
        _spatialMotion = source != null && _transition.Animated;
        if (source != null)
        {
            _sourceOpacity =
                source.GetComponent<CanvasGroup>() ?? source.gameObject.AddComponent<CanvasGroup>();
            _sourceAlpha = _sourceOpacity.alpha;
            _sourcePosition = _slot.parent.InverseTransformPoint(source.position);
            var parentScale = _slot.parent.lossyScale;
            var sourceScale = source.lossyScale;
            _sourceScale = new Vector3(
                sourceScale.x / parentScale.x,
                sourceScale.y / parentScale.y,
                1f
            );
        }
        _pendingSeconds = 0f;
        ApplyTransition();
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
            ("tier", card.StartingTier),
            ("enchantable", card.IsEnchantable),
            ("scale", Math.Round(scale, 3)),
            ("animated", _transition.Animated),
            ("shared_origin", _spatialMotion)
        );
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        if (!IsOpen)
            return false;
        _transition.Cancel();
        if (_sourceOpacity != null)
            _sourceOpacity.alpha = _sourceAlpha;
        _sourceOpacity = null;
        Return();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspection_closed"),
            ("reason", reason),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
        return true;
    }

    public bool HandleBack() => Dismiss("back_key");

    private bool Dismiss(string reason)
    {
        if (!IsOpen)
            return false;
        _dismissReason = reason;
        _transition.Dismiss();
        _detailInteraction.interactable = false;
        if (_transition.Finished)
            Close(reason);
        return true;
    }

    public void Tick()
    {
        if (!IsOpen)
            return;
        if (!_transition.Pending && !_transition.Closing && _transition.Progress >= 1f)
            return;
        if (_transition.Pending)
        {
            _pendingSeconds += Time.unscaledDeltaTime;
            if (_lease?.State == CardRentalLeaseState.Pending && _pendingSeconds < 2f)
                return;
            if (_lease?.State != CardRentalLeaseState.Ready)
            {
                // A failed or stalled rental must not leave an invisible modal intercepting taps.
                Close("preview_unavailable");
                return;
            }
            if (_sourceOpacity != null)
                _sourceOpacity.alpha = 0f;
            _transition.Ready();
            // This delta includes the previous frame's native setup. Render the origin first;
            // only subsequent frames spend the transition's duration.
            ApplyTransition();
            return;
        }
        _transition.Tick(Time.unscaledDeltaTime);
        ApplyTransition();
        if (_transition.Finished)
            Close(_dismissReason);
    }

    private void ApplyTransition()
    {
        var progress = _transition.Progress;
        _slot.localPosition = _spatialMotion
            ? Vector3.Lerp(_sourcePosition, _previewPosition, progress)
            : _previewPosition;
        _slot.localScale = _spatialMotion
            ? Vector3.Lerp(_sourceScale, _previewScale, progress)
            : _previewScale;
        _cardOpacity.alpha = _transition.Pending ? 0f : 1f;
        _backdrop.color = InGameTheme.WithAlpha(InGameTheme.Scrim, InGameTheme.Scrim.a * progress);
        _infoOpacity.alpha = Mathf.Clamp01((progress - 0.15f) / 0.85f);
        _detailInteraction.interactable = progress >= 1f && !_transition.Closing;
    }

    private void Return()
    {
        AndroidCardRental.Return(_lease);
        _lease = null;
        _card = null;
    }

    private CardRentalSubject Subject() => new(_card!.Id, _card.StartingTier);

    private void SetEnchantmentsVisible(bool visible)
    {
        _showEnchantments = visible;
        if (visible && _enchantmentEffects == null)
        {
            _enchantmentEffects = new();
            // One template for every enchantment and tier; each tier still builds its own tooltip.
            var template = AndroidCardTemplateText.Template(_card!.Id);
            foreach (var enchantment in _card.Enchantments.Keys.OrderBy(value => value))
            {
                var effects = new List<CollectionTierTooltipText>();
                foreach (var tier in ReachableTiers(_card.StartingTier))
                {
                    var effect = AndroidCardTemplateText.EnchantmentEffect(
                        template,
                        new CardRentalSubject(_card.Id, tier),
                        enchantment
                    );
                    if (!string.IsNullOrWhiteSpace(effect))
                        effects.Add(new CollectionTierTooltipText(tier, effect));
                }
                _enchantmentEffects.Add(
                    (
                        enchantment,
                        effects.Count == 0
                            ? L.Resolve(
                                new LocalizedTextSet(
                                    "Effect unavailable",
                                    "效果暂不可用",
                                    "效果暫不可用"
                                )
                            )
                            : CollectionTierTooltipTextMerger.MergeLines(
                                effects,
                                keepValuesTogether: true
                            )
                    )
                );
            }
        }
        // Keep the toggle in place when expanding; collapsing clamps to the remaining content.
        _detailScroll.StopMovement();
        RenderDetail();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.enchantments_toggled"),
            ("visible", visible),
            ("count", _enchantmentEffects?.Count ?? 0)
        );
    }

    private void UpdatePreview()
    {
        var card = _card!;
        var texts = new List<CollectionTierTooltipText>();
        var cooldowns = new List<CollectionTierTooltipText>();
        var ammunition = new List<CollectionTierTooltipText>();
        foreach (var tier in ReachableTiers(card.StartingTier))
        {
            // One template and one tooltip per tier for the text and its values.
            var (text, cooldown, ammo) = AndroidCardTemplateText.Preview(
                new CardRentalSubject(card.Id, tier)
            );
            if (text != null)
                texts.Add(new CollectionTierTooltipText(tier, text));
            if (cooldown is > 0)
                cooldowns.Add(
                    new CollectionTierTooltipText(
                        tier,
                        cooldown.Value.ToString("0.#", CultureInfo.InvariantCulture)
                    )
                );
            if (ammo is >= 0)
                ammunition.Add(
                    new CollectionTierTooltipText(
                        tier,
                        ammo.Value.ToString("0", CultureInfo.InvariantCulture)
                    )
                );
        }
        _text =
            texts.Count == 0
                ? card.Description
                : CollectionTierTooltipTextMerger.MergeLines(texts, keepValuesTogether: true);
        _cooldown = CollectionTierTooltipTextMerger.Merge(cooldowns);
        _ammo = CollectionTierTooltipTextMerger.Merge(ammunition);
    }

    // Base values stay visible while the optional list expands beneath them.
    private void RenderDetail()
    {
        for (var index = _detail.childCount - 1; index >= 0; index--)
        {
            var child = _detail.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
        var card = _card;
        if (card == null)
            return;
        var chrome = _layout.Chrome;
        var flow = new AndroidChipFlow(_detail, _font, chrome, 1, _layout.RailHeaderHeight, _skin);
        var identity = card.Tags.Distinct().Select(AndroidCollectionLabels.InspectionTag).ToList();
        if (card.Type == CardType.Item)
            identity.Insert(0, CollectionPanelText.Size(card.Size));
        if (identity.Count > 0)
            flow.Tags(identity);
        var stats = new List<(string Label, string Value, float Weight)>();
        if (_cooldown.Length > 0)
            stats.Add(
                (
                    L.Resolve(new LocalizedTextSet("Cooldown", "冷却", "冷卻")),
                    $"{_cooldown}<size=65%> s</size>",
                    3f
                )
            );
        if (_ammo.Length > 0)
            stats.Add((L.Resolve(new LocalizedTextSet("Ammo", "弹药", "彈藥")), _ammo, 1f));
        if (stats.Count > 0)
        {
            flow.Attributes(stats);
            flow.Divider();
        }
        if (!string.IsNullOrWhiteSpace(_text))
            flow.Note(_text, AndroidUiKit.TextColor, chrome.ButtonFont);
        if (card.IsEnchantable && card.Enchantments.Count > 0)
        {
            flow.Divider();
            flow.Toggle(
                L.Resolve(new LocalizedTextSet("Enchantment effects", "附魔效果", "附魔效果")),
                _showEnchantments,
                SetEnchantmentsVisible
            );
            if (_showEnchantments && _enchantmentEffects != null)
                flow.Definitions(
                    _enchantmentEffects
                        .Select(entry =>
                            (
                                $"<color=#{ItemEnchantPreviewText.ColorHex(entry.Type.ToString())}>{AndroidCollectionLabels.Enchantment(entry.Type)}</color>",
                                entry.Text
                            )
                        )
                        .ToList()
                );
        }
        _detail.sizeDelta = new Vector2(0f, flow.Height + chrome.Gap);
    }

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
