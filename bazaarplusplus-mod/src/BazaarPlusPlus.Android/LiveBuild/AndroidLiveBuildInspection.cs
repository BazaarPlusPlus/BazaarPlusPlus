using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// A recommended build's card, tapped, enlarged over the panel with its name and its text as the
/// game's tooltip renders it (<see cref="AndroidCardTemplateText.Description(CardRentalSubject)"/>):
/// the touch counterpart of hovering a card on the desktop, where the game's own tooltip shows.
/// Android has no hover tooltip, so the text stands beside the card as in the Collection
/// inspection. The card is the game's own prefab, rented at the build's tier and enchantment. A
/// tap outside, the close button, or the back key dismisses it, and the lease goes back to the
/// rental.
/// </summary>
internal sealed class AndroidLiveBuildInspection
{
    public const string RentalOwner = "live_build.inspection";

    private readonly GameObject _root;
    private readonly RectTransform _slot;
    private readonly TextMeshProUGUI _name;
    private readonly TextMeshProUGUI _description;
    private readonly LiveBuildTouchLayout _layout;
    private CardRentalLease<CardPreviewBase>? _lease;

    public AndroidLiveBuildInspection(
        RectTransform parent,
        TMP_FontAsset font,
        LiveBuildTouchLayout layout
    )
    {
        _layout = layout;
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
        AndroidUiActions.OnTap(dismiss, "live_build.inspection", () => Close("outside_tap"));
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

        var info = AndroidTouchScreen.Child("Info", root, layout.InspectInfo);
        // Opaque, so the panel's text under it does not show through.
        var panel = AndroidUiKit.PanelColor;
        panel.a = 1f;
        AndroidUiKit.Fill(info, panel);
        // Swallows taps on the panel, which would otherwise reach the dismiss button behind it.
        var swallow = info.gameObject.AddComponent<Button>();
        swallow.transition = Selectable.Transition.None;
        var origin = layout.InspectInfo;
        _name = AndroidUiKit.Text(
            AndroidTouchScreen.Child("Name", info, Local(layout.InspectName, origin, layout.Gap)),
            font,
            "",
            layout.NameFont,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        _name.overflowMode = TextOverflowModes.Ellipsis;
        _description = AndroidUiKit.Text(
            AndroidTouchScreen.Child(
                "Description",
                info,
                Local(layout.InspectDetail, origin, layout.Gap)
            ),
            font,
            "",
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidUiKit.MutedTextColor
        );
        _description.overflowMode = TextOverflowModes.Ellipsis;
        var close = AndroidTouchScreen.Child("Close", info, Local(layout.InspectClose, origin, 0f));
        AndroidUiKit.CloseButton(close, font, layout.TitleFont, () => Close("close_button"));
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    /// <summary>The inspected card's lease, Pending or Ready: one while open.</summary>
    public int Leases => _lease != null ? 1 : 0;

    /// <summary>The inspected card is still being instantiated or set up.</summary>
    public bool SettingUp => _lease is { State: CardRentalLeaseState.Pending };

    public void Open(BppItemBoardCard card, string source)
    {
        Return();
        var subject = new CardRentalSubject(card.TemplateId, card.Tier, card.EnchantmentType);
        _name.text = AndroidCardTemplateText.Title(card.TemplateId) ?? string.Empty;
        _description.text = AndroidCardTemplateText.Description(subject) ?? string.Empty;
        var scale = _layout.InspectScale(card.DisplaySpan);
        _slot.localScale = new Vector3(scale, scale, 1f);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _lease = AndroidCardRental.Rent(subject, _slot, RentalOwner);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.inspected"),
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
        Return();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.inspection_closed"),
            ("reason", reason),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
        return true;
    }

    private void Return()
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
