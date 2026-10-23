using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The tapped card, enlarged over the grid, with the tiers it can reach and the enchantments it
/// takes. A chip sets the same rented card up again at that tier or enchantment. The desktop's
/// inspection locks the game's own tooltip beside the card; Android has no hover tooltip to lock,
/// so the card's name and authored text stand beside it instead.
/// </summary>
internal sealed class AndroidCollectionInspection
{
    public const string RentalOwner = "collection.inspection";

    private const float CardHeight = 640f;

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly RectTransform _chips;
    private readonly TextMeshProUGUI _name;
    private readonly TextMeshProUGUI _description;
    private readonly TMP_FontAsset _font;
    private CollectionCardVm? _card;
    private CardRentalLease<CardPreviewBase>? _lease;
    private CardTier _tier;
    private CardEnchantment? _enchantment;

    public AndroidCollectionInspection(RectTransform parent, TMP_FontAsset font)
    {
        _font = font;
        var root = AndroidUiKit.Rect(
            "Inspection",
            parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        AndroidUiKit.Fill(root, new Color(0f, 0f, 0f, 0.82f));
        var dismiss = root.gameObject.AddComponent<UnityEngine.UI.Button>();
        dismiss.transition = UnityEngine.UI.Selectable.Transition.None;
        dismiss.onClick.AddListener((UnityEngine.Events.UnityAction)(() => Close("outside_tap")));
        _root = root.gameObject;

        _slot = AndroidUiKit.Fixed("CardSlot", root, new(0.3f, 0.5f), Vector2.zero, Vector2.zero);

        var info = AndroidUiKit.Rect(
            "Info",
            root,
            new(0.55f, 0f),
            new(1f, 1f),
            new(0f, 48f),
            new(-48f, -48f)
        );
        AndroidUiKit.Fill(info, AndroidUiKit.PanelColor);
        // Swallows taps on the text, which would otherwise reach the dismiss button behind it.
        var swallow = info.gameObject.AddComponent<UnityEngine.UI.Button>();
        swallow.transition = UnityEngine.UI.Selectable.Transition.None;
        var name = AndroidUiKit.Rect(
            "Name",
            info,
            new(0f, 1f),
            new(1f, 1f),
            new(24f, -92f),
            new(-132f, -16f)
        );
        _name = AndroidUiKit.Text(
            name,
            font,
            "",
            38f,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        var close = AndroidUiKit.Fixed("Close", info, new(1f, 1f), new(-16f, -16f), new(100f, 68f));
        AndroidUiKit.Button(close, font, "X", 34f, () => Close("close_button"));
        _chips = AndroidUiKit.Rect(
            "Chips",
            info,
            new(0f, 1f),
            new(1f, 1f),
            new(24f, -312f),
            new(-24f, -108f)
        );
        var description = AndroidUiKit.Rect(
            "Description",
            info,
            Vector2.zero,
            Vector2.one,
            new(24f, 24f),
            new(-24f, -328f)
        );
        _description = AndroidUiKit.Text(
            description,
            font,
            "",
            26f,
            TextAlignmentOptions.TopLeft,
            AndroidUiKit.MutedTextColor
        );
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public void Open(CollectionCardVm card)
    {
        Return();
        _card = card;
        _tier = card.StartingTier;
        _enchantment = null;
        _name.text = card.DisplayName;
        _description.text = card.Description;
        var bounds = AndroidCollectionGrid.NaturalBounds(card);
        var scale = CardHeight / bounds.Height;
        _slot.localScale = new Vector3(
            card.Type == CardType.Item ? scale * CollectionGridConstants.ItemCardWidthScale : scale,
            scale,
            1f
        );
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _lease = AndroidCardRental.Rent(Subject(), _slot, RentalOwner);
        RenderChips();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspected"),
            ("type", card.Type),
            ("size", card.Size),
            ("tier", _tier),
            ("enchantable", card.IsEnchantable)
        );
    }

    public void Close(string reason)
    {
        if (!IsOpen)
            return;
        Return();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspection_closed"),
            ("reason", reason)
        );
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
        RenderChips();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.inspection_retargeted"),
            ("tier", tier),
            ("enchantment", enchantment)
        );
    }

    private void RenderChips()
    {
        for (var index = _chips.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_chips.GetChild(index).gameObject);
        var card = _card;
        if (card == null)
            return;
        var flow = new AndroidChipFlow(_chips, _font, columns: 5, chipHeight: 60f, fontSize: 24f);
        foreach (var tier in ReachableTiers(card.StartingTier))
        {
            var chosen = tier;
            flow.Chip(
                CollectionPanelText.Tier(tier),
                tier == _tier,
                () => Select(chosen, _enchantment)
            );
        }
        if (!card.IsEnchantable || card.Enchantments.Count == 0)
            return;
        flow.Break();
        flow.Chip("-", _enchantment == null, () => Select(_tier, null));
        foreach (var enchantment in card.Enchantments.Keys.OrderBy(value => value))
        {
            var chosen = enchantment;
            flow.Chip(
                AndroidCollectionLabels.Enchantment(enchantment),
                enchantment == _enchantment,
                () => Select(_tier, chosen)
            );
        }
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
}
