using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// A History board card or skill, tapped, enlarged over the panel with its name and authored
/// text: the touch counterpart of hovering a card on the desktop's native board, where the game's
/// own tooltip shows. The card is the game's own prefab, rented at the battle's tier and
/// enchantment. A tap outside, the close button, or the back key dismisses it, and the lease goes
/// back to the rental.
/// </summary>
internal sealed class AndroidHistoryInspection
{
    public const string RentalOwner = "history.inspection";

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly TextMeshProUGUI _name;
    private readonly TextMeshProUGUI _description;
    private readonly HistoryTouchLayout _layout;
    private CardRentalLease<CardPreviewBase>? _lease;

    public AndroidHistoryInspection(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout
    )
    {
        _layout = layout;
        var root = AndroidHistoryUi.Overlay(
            parent,
            "Inspection",
            "inspection_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var area = AndroidTouchScreen.Child("CardArea", root, layout.InspectCardArea);
        _slot = AndroidUiKit.Rect(
            "CardSlot",
            area,
            new(0.5f, 0.5f),
            new(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );
        var info = AndroidHistoryUi.Panel(root, "Info", layout.InspectInfo);
        var origin = layout.InspectInfo;
        _name = AndroidHistoryUi.Label(
            info,
            font,
            "Name",
            Local(layout.InspectName, origin, layout.Gap),
            layout.NameFont,
            wrap: true
        );
        _description = AndroidHistoryUi.Label(
            info,
            font,
            "Description",
            Local(layout.InspectDetail, origin, layout.Gap),
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        AndroidHistoryUi.Button(
            info,
            font,
            "inspection_close",
            Local(layout.InspectClose, origin, 0f),
            layout.TitleFont,
            () => Close("close_button"),
            AndroidUiKit.CloseGlyph
        );
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public int Leases => _lease != null ? 1 : 0;

    public void Open(BppItemBoardCard card, bool skill, string source)
    {
        Return();
        var subject = new CardRentalSubject(card.TemplateId, card.Tier, card.EnchantmentType);
        _name.text = AndroidCardTemplateText.Title(card.TemplateId) ?? string.Empty;
        _description.text = AndroidCardTemplateText.Description(subject) ?? string.Empty;
        AndroidKeywordSprites.Apply(_description);
        var scale = _layout.InspectScale(card.DisplaySpan, skill);
        _slot.localScale = new Vector3(scale, scale, 1f);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _lease = AndroidCardRental.Rent(subject, _slot, RentalOwner);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.card_inspected"),
            ("source", source),
            ("skill", skill),
            ("size", card.Size),
            ("tier", card.Tier),
            ("scale", Math.Round(scale, 3))
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.inspection_closed"),
            ("reason", reason),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
        return true;
    }

    /// <summary>The lease goes back without a log line: the panel is closing or laid out again.</summary>
    public void Return()
    {
        AndroidCardRental.Return(_lease);
        _lease = null;
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
