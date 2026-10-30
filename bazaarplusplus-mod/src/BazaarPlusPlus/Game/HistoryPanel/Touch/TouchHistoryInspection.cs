#nullable enable
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Touch;

/// <summary>
/// A History board card, tapped, enlarged over the panel with its name and authored
/// text: the touch counterpart of hovering a card on the desktop's native board, where the game's
/// own tooltip shows. The card is the game's own prefab, rented at the battle's tier and
/// enchantment. A tap outside, the close button, or the back key dismisses it, and the lease goes
/// back to the rental.
/// </summary>
internal sealed class TouchHistoryInspection : IHistoryCardOverlay
{
    public const string RentalOwner = "history.inspection";

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly TextMeshProUGUI _name;
    private readonly InspectionText _description;
    private readonly HistoryTouchLayout _layout;
    private CardRentalLease<CardPreviewBase>? _lease;

    public TouchHistoryInspection(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout
    )
    {
        _layout = layout;
        var root = TouchPanelUi.Overlay(
            parent,
            "Inspection",
            "inspection_outside",
            () => Close("outside_tap")
        );
        _root = root.gameObject;
        var area = TouchScreen.Child("CardArea", root, layout.InspectCardArea);
        _slot = TouchUiKit.Rect(
            "CardSlot",
            area,
            new(0.5f, 0.5f),
            new(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );
        var info = TouchPanelUi.Panel(root, "Info", layout.InspectInfo);
        var origin = layout.InspectInfo;
        _name = TouchPanelUi.Label(
            info,
            font,
            "Name",
            Local(layout.InspectName, origin, layout.Gap),
            layout.NameFont,
            wrap: true
        );
        _description = new InspectionText(
            TouchScreen.Child("Description", info, Local(layout.InspectDetail, origin, layout.Gap)),
            font,
            layout.BodyFont,
            TouchPanelUi.Ink
        );
        TouchPanelUi.Button(
            info,
            font,
            "inspection_close",
            Local(layout.InspectClose, origin, 0f),
            layout.TitleFont,
            () => Close("close_button"),
            TouchUiKit.CloseGlyph
        );
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public int Leases => _lease != null ? 1 : 0;

    public void Open(BppItemBoardCard card, string source)
    {
        ReturnCards();
        var subject = new CardRentalSubject(card.TemplateId, card.Tier, card.EnchantmentType);
        _name.text = TouchCardText.Title(card.TemplateId) ?? string.Empty;
        _description.Set(TouchCardText.Description(subject) ?? string.Empty);
        var scale = _layout.InspectScale(card.DisplaySpan);
        _slot.localScale = new Vector3(scale, scale, 1f);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _lease = TouchCardRental.Rent(subject, _slot, RentalOwner);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.card_inspected"),
            ("source", source),
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
        ReturnCards();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.inspection_closed"),
            ("reason", reason),
            ("rental_leases", TouchCardRental.ActiveLeases)
        );
        return true;
    }

    /// <summary>The lease goes back without a log line: the panel is closing or laid out again.</summary>
    public void ReturnCards()
    {
        TouchCardRental.Return(_lease);
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
